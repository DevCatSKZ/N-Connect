using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Switch2Pro.Protocol;

/// <summary>Ein Controller in der Übertragungsdatei.</summary>
public sealed record TransferController
{
    public string Address { get; init; } = "";
    /// <summary>Anzeigename (z. B. „Joy-Con (L)“), falls bekannt.</summary>
    public string? Name { get; init; }
    /// <summary>Adresse des Hosts (PC-Adapter oder Switch), an den der Controller gebunden ist.</summary>
    public string? HostAddress { get; init; }
    /// <summary>Herkunft: "N-Connect" (dieser PC) oder "Switch" (SD-Karte).</summary>
    public string Source { get; init; } = "N-Connect";
    /// <summary>Kopplungsschlüssel (16 Byte, Hex) – nur, wenn er beim Export ausdrücklich mitgenommen wurde.</summary>
    public string? LinkKey { get; init; }
}

/// <summary>Inhalt der Übertragungsdatei „Kopplungsdaten“ (PC → PC).</summary>
public sealed record PairingExport
{
    public DateTime Created { get; init; } = DateTime.UtcNow;
    /// <summary>Name des PCs, auf dem exportiert wurde.</summary>
    public string? SourcePc { get; init; }
    /// <summary>Bluetooth-Adresse des Adapters dieses PCs beim Export.</summary>
    public string? AdapterAddress { get; init; }
    public List<TransferController> Controllers { get; init; } = [];
    public List<string> KnownControllers { get; init; } = [];
    public List<string> AllowedControllers { get; init; } = [];
    public List<string> SingleJoyCons { get; init; } = [];
    public List<string> UprightJoyCons { get; init; } = [];
    public Dictionary<string, GyroBias> GyroCalibration { get; init; } = [];

    public bool HasKeys => Controllers.Any(c => !string.IsNullOrEmpty(c.LinkKey));
}

/// <summary>Warum eine Übertragungsdatei nicht gelesen werden konnte.</summary>
public enum PairingFileError { NotAPairingFile, NewerVersion, PasswordRequired, WrongPassword, Corrupt }

public sealed class PairingFileException(PairingFileError error, string message) : Exception(message)
{
    public PairingFileError Error { get; } = error;
}

/// <summary>
/// Datei „*.ncpair“: JSON mit Kennung und Version. Ohne Passwort steht der Inhalt lesbar darin; mit Passwort ist er
/// mit AES-256-GCM verschlüsselt, der Schlüssel per PBKDF2-SHA256 aus dem Passwort abgeleitet (eigenes Salz je Datei).
/// GCM erkennt dabei auch jede Veränderung der Datei.
/// </summary>
public static class PairingTransfer
{
    public const string FormatId = "n-connect-pairings";
    public const int Version = 1;
    public const string FileExtension = ".ncpair";
    private const int Iterations = 200_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write(PairingExport data, string? password)
    {
        var payload = JsonSerializer.SerializeToNode(data, Json)!;
        var root = new JsonObject { ["format"] = FormatId, ["version"] = Version };
        if (string.IsNullOrEmpty(password))
        {
            root["encrypted"] = false;
            root["payload"] = payload;
        }
        else
        {
            byte[] plain = Encoding.UTF8.GetBytes(payload.ToJsonString());
            byte[] salt = RandomNumberGenerator.GetBytes(16), nonce = RandomNumberGenerator.GetBytes(12);
            byte[] cipher = new byte[plain.Length], tag = new byte[16];
            using (var aes = new AesGcm(DeriveKey(password, salt, Iterations), tag.Length))
                aes.Encrypt(nonce, plain, cipher, tag, Header());
            CryptographicOperations.ZeroMemory(plain);
            root["encrypted"] = true;
            root["kdf"] = "PBKDF2-SHA256";
            root["iterations"] = Iterations;
            root["salt"] = Convert.ToBase64String(salt);
            root["nonce"] = Convert.ToBase64String(nonce);
            root["tag"] = Convert.ToBase64String(tag);
            root["data"] = Convert.ToBase64String(cipher);
        }
        return root.ToJsonString(Json);
    }

    /// <summary>Ist die Datei passwortgeschützt? (false auch bei unlesbaren Dateien – <see cref="Read"/> meldet den Fehler.)</summary>
    public static bool IsEncrypted(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject o && o["encrypted"]?.GetValue<bool>() == true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Liest die Datei; wirft <see cref="PairingFileException"/> mit dem Grund, wenn das nicht geht.</summary>
    public static PairingExport Read(string text, string? password)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(text) as JsonObject ?? throw new PairingFileException(PairingFileError.NotAPairingFile, "Keine Kopplungsdaten-Datei.");
        }
        catch (JsonException)
        {
            // Abgeschnittene oder veränderte Datei: an der Kennung trotzdem als Kopplungsdaten erkennbar.
            throw text.Contains(FormatId, StringComparison.Ordinal)
                ? new PairingFileException(PairingFileError.Corrupt, "Die Datei ist beschädigt.")
                : new PairingFileException(PairingFileError.NotAPairingFile, "Keine Kopplungsdaten-Datei.");
        }
        try
        {
            if (root["format"]?.GetValue<string>() != FormatId)
                throw new PairingFileException(PairingFileError.NotAPairingFile, "Keine Kopplungsdaten-Datei.");
            if ((root["version"]?.GetValue<int>() ?? 0) > Version)
                throw new PairingFileException(PairingFileError.NewerVersion, "Die Datei stammt von einer neueren N-Connect-Version.");
            JsonNode? payload;
            if (root["encrypted"]?.GetValue<bool>() == true)
            {
                if (string.IsNullOrEmpty(password))
                    throw new PairingFileException(PairingFileError.PasswordRequired, "Die Datei ist mit einem Passwort geschützt.");
                int iterations = root["iterations"]?.GetValue<int>() ?? 0;
                if (iterations is < 10_000 or > 10_000_000)
                    throw new PairingFileException(PairingFileError.Corrupt, "Die Datei ist beschädigt.");
                byte[] salt = B64(root, "salt"), nonce = B64(root, "nonce"), tag = B64(root, "tag"), cipher = B64(root, "data");
                if (nonce.Length != 12 || tag.Length != 16 || salt.Length < 8)
                    throw new PairingFileException(PairingFileError.Corrupt, "Die Datei ist beschädigt.");
                byte[] plain = new byte[cipher.Length];
                try
                {
                    using var aes = new AesGcm(DeriveKey(password, salt, iterations), tag.Length);
                    aes.Decrypt(nonce, cipher, tag, plain, Header());
                }
                catch (AuthenticationTagMismatchException)
                {
                    // Falsches Passwort und veränderte Datei sind hier nicht zu unterscheiden.
                    throw new PairingFileException(PairingFileError.WrongPassword, "Falsches Passwort oder die Datei wurde verändert.");
                }
                payload = JsonNode.Parse(plain);
                CryptographicOperations.ZeroMemory(plain);
            }
            else
            {
                payload = root["payload"];
            }
            var data = payload?.Deserialize<PairingExport>(Json)
                       ?? throw new PairingFileException(PairingFileError.Corrupt, "Die Datei ist beschädigt.");
            return Sanitized(data);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            throw new PairingFileException(PairingFileError.Corrupt, "Die Datei ist beschädigt.");
        }
    }

    /// <summary>Was N-Connect auf diesem PC über Controller weiß (ohne Schlüssel – die kennt N-Connect nicht).</summary>
    public static PairingExport FromSettings(Settings settings, string? adapterAddress, string? pcName, IEnumerable<TransferController>? extra = null)
    {
        var controllers = new List<TransferController>();
        foreach (var address in settings.KnownControllers.Concat(settings.ImportedPairings.Select(p => p.Address)))
        {
            if (!BtAddress.TryNormalize(address, out var a) || controllers.Any(c => c.Address == a))
                continue;
            var note = settings.ImportedPairings.FirstOrDefault(p => BtAddress.Same(p.Address, a));
            controllers.Add(new TransferController
            {
                Address = a,
                Name = note?.Name,
                HostAddress = note?.HostAddress ?? adapterAddress,
                Source = note?.Source ?? "N-Connect",
            });
        }
        foreach (var c in extra ?? [])
        {
            controllers.RemoveAll(x => x.Address == c.Address);
            controllers.Add(c);
        }
        return new PairingExport
        {
            SourcePc = pcName,
            AdapterAddress = adapterAddress,
            Controllers = controllers,
            KnownControllers = [.. settings.KnownControllers],
            AllowedControllers = [.. settings.AllowedControllers],
            SingleJoyCons = [.. settings.SingleJoyCons],
            UprightJoyCons = [.. settings.UprightJoyCons],
            GyroCalibration = new Dictionary<string, GyroBias>(settings.GyroCalibration),
        };
    }

    /// <summary>
    /// Übernimmt die Daten in die Einstellungen, ohne Vorhandenes zu löschen: Listen werden ergänzt, eigene
    /// Gyro-Kalibrierungen bleiben. Eine leere Freigabeliste („alle Controller erlaubt“) bleibt leer, damit der
    /// Import keine Controller aussperrt. Schlüssel werden nicht gespeichert. Gibt die Zahl der Änderungen zurück.
    /// </summary>
    public static int Merge(Settings target, PairingExport data)
    {
        int changes = 0;
        List<string> Union(List<string> list, IEnumerable<string> add)
        {
            var result = new List<string>(list);
            foreach (var raw in add)
            {
                if (BtAddress.TryNormalize(raw, out var a) && !result.Any(x => BtAddress.Same(x, a)))
                {
                    result.Add(a);
                    changes++;
                }
            }
            return result;
        }
        target.KnownControllers = Union(target.KnownControllers, data.KnownControllers.Concat(data.Controllers.Select(c => c.Address)));
        if (target.AllowedControllers.Count > 0)
            target.AllowedControllers = Union(target.AllowedControllers, data.AllowedControllers.Concat(data.Controllers.Select(c => c.Address)));
        target.SingleJoyCons = Union(target.SingleJoyCons, data.SingleJoyCons);
        target.UprightJoyCons = Union(target.UprightJoyCons, data.UprightJoyCons);
        var gyro = new Dictionary<string, GyroBias>(target.GyroCalibration, StringComparer.OrdinalIgnoreCase);
        foreach (var (address, bias) in data.GyroCalibration)
        {
            if (gyro.TryAdd(address, bias))
                changes++;
        }
        target.GyroCalibration = gyro;
        var notes = new List<PairingNote>(target.ImportedPairings);
        foreach (var c in data.Controllers)
        {
            if (!BtAddress.TryNormalize(c.Address, out var a))
                continue;
            var note = new PairingNote(a, c.Name, BtAddress.TryNormalize(c.HostAddress, out var h) ? h : null, c.Source);
            int i = notes.FindIndex(n => BtAddress.Same(n.Address, a));
            if (i < 0)
            {
                notes.Add(note);
                changes++;
            }
            else if (notes[i] != note)
            {
                notes[i] = note;
                changes++;
            }
        }
        target.ImportedPairings = notes;
        return changes;
    }

    /// <summary>Kopplungsdaten von der Switch-SD-Karte als Übertragungseinträge (Schlüssel nur auf Wunsch).</summary>
    public static List<TransferController> FromSwitch(SwitchPairingData data, bool includeKeys) =>
        data.Valid.Select(c => new TransferController
        {
            Address = c.ControllerAddress,
            Name = c.DisplayName,
            HostAddress = c.HostAddress,
            Source = "Switch",
            LinkKey = includeKeys ? c.LinkKeyHex : null,
        }).ToList();

    private static PairingExport Sanitized(PairingExport data)
    {
        static List<string> Clean(List<string>? list) =>
            (list ?? []).Select(a => BtAddress.TryNormalize(a, out var n) ? n : null).OfType<string>().Distinct().ToList();
        return data with
        {
            Controllers = (data.Controllers ?? []).Where(c => c is not null && BtAddress.TryNormalize(c.Address, out _))
                .Select(c => c with
                {
                    Address = BtAddress.TryNormalize(c.Address, out var a) ? a : c.Address,
                    LinkKey = c.LinkKey is { Length: 32 } k && k.All(Uri.IsHexDigit) ? k.ToUpperInvariant() : null,
                    Source = c.Source ?? "N-Connect",
                }).ToList(),
            KnownControllers = Clean(data.KnownControllers),
            AllowedControllers = Clean(data.AllowedControllers),
            SingleJoyCons = Clean(data.SingleJoyCons),
            UprightJoyCons = Clean(data.UprightJoyCons),
            GyroCalibration = (data.GyroCalibration ?? [])
                .Where(g => BtAddress.TryNormalize(g.Key, out _) && float.IsFinite(g.Value.X) && float.IsFinite(g.Value.Y) && float.IsFinite(g.Value.Z))
                .ToDictionary(g => g.Key, g => g.Value),
        };
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>Zusatzdaten für GCM: bindet Kennung und Version an den verschlüsselten Inhalt.</summary>
    private static byte[] Header() => Encoding.ASCII.GetBytes($"{FormatId}/{Version}");

    private static byte[] B64(JsonObject root, string name) =>
        Convert.FromBase64String(root[name]?.GetValue<string>() ?? throw new FormatException(name));
}

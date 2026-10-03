using System.Security.Cryptography;

namespace Switch2Pro.Protocol;

/// <summary>
/// Nintendos eigenes Kopplungsverfahren für Switch-2-Controller (Befehl 0x15), nach der Beschreibung in
/// ndeadly/switch2_controller_research (bluetooth_interface.md, „Pairing“):
/// 1. Host-Adressen übergeben (0x01), 2. Schlüsselteile tauschen (0x04): LTK = A1 XOR B1,
/// 3. Bestätigung (0x02): Controller verschlüsselt die Aufgabe A2 mit dem LTK (AES-128-ECB, Bytes umgedreht),
/// 4. Abschluss (0x03): Controller speichert Adressen und LTK.
/// Danach wirbt der Controller nach einem Tastendruck gezielt für diesen Host. Der Controller merkt sich
/// genau einen Host – eine bestehende Kopplung (z. B. mit der Switch 2) wird ersetzt.
/// Alle Werte werden so übertragen, wie sie hier stehen (Doku: „reverse byte-order“).
/// </summary>
public sealed class Pairing
{
    public const byte Command = 0x15;
    public const byte SubAddresses = 0x01;
    public const byte SubConfirm = 0x02;
    public const byte SubFinish = 0x03;
    public const byte SubKeys = 0x04;

    public byte[] HostKey { get; }
    public byte[] Challenge { get; }

    public Pairing() : this(RandomNumberGenerator.GetBytes(16), RandomNumberGenerator.GetBytes(16)) { }

    public Pairing(byte[] hostKey, byte[] challenge)
    {
        if (hostKey.Length != 16 || challenge.Length != 16)
            throw new ArgumentException("Schlüssel und Aufgabe müssen 16 Byte lang sein.");
        HostKey = hostKey;
        Challenge = challenge;
    }

    /// <summary>Schritt 1: Host-Adresse (z. B. Bluetooth-Adapter des PCs) zweimal, niedrigstes Byte zuerst.</summary>
    public static byte[] AddressesRequest(ulong hostAddress)
    {
        var data = new byte[2 + 12];
        data[1] = 2;
        for (int i = 0; i < 6; i++)
            data[2 + i] = data[8 + i] = (byte)(hostAddress >> (8 * i));
        return Commands.Build(Command, SubAddresses, data);
    }

    public byte[] KeysRequest() => Commands.Build(Command, SubKeys, [0x00, .. HostKey]);

    public byte[] ConfirmRequest() => Commands.Build(Command, SubConfirm, [0x00, .. Challenge]);

    public static byte[] FinishRequest() => Commands.Build(Command, SubFinish, [0x00]);

    /// <summary>Gemeinsamer Schlüssel aus der Antwort auf <see cref="KeysRequest"/>.</summary>
    public bool TryDeriveKey(ReadOnlySpan<byte> keysResponse, out byte[] ltk)
    {
        ltk = [];
        if (!Commands.IsResponseTo(keysResponse, Command, SubKeys) || keysResponse.Length < 8 + 17)
            return false;
        var deviceKey = keysResponse.Slice(9, 16);
        ltk = new byte[16];
        for (int i = 0; i < 16; i++)
            ltk[i] = (byte)(HostKey[i] ^ deviceKey[i]);
        return true;
    }

    /// <summary>Erwartete Antwort B2 = AES-128-ECB(LTK, A2); LTK und A2 werden vorher umgedreht.</summary>
    public static byte[] ExpectedConfirmation(byte[] ltk, byte[] challenge)
    {
        using var aes = Aes.Create();
        aes.Key = ltk.Reverse().ToArray();
        // Mit dem Mitschnitt der Konsole geprüft: Ergebnis wird so übertragen, wie AES es liefert.
        return aes.EncryptEcb(challenge.Reverse().ToArray(), PaddingMode.None);
    }

    /// <summary>Prüft, ob der Controller denselben Schlüssel berechnet hat.</summary>
    public bool VerifyConfirmation(ReadOnlySpan<byte> confirmResponse, byte[] ltk) =>
        Commands.IsResponseTo(confirmResponse, Command, SubConfirm) && confirmResponse.Length >= 8 + 17
        && confirmResponse.Slice(9, 16).SequenceEqual(ExpectedConfirmation(ltk, Challenge));
}

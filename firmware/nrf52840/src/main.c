/*
 * N-Connect-Funkadapter für den nRF52840-Dongle (Zephyr / nRF Connect SDK), Weg A.
 *
 * Der Dongle gibt sich als der PC-Bluetooth-Adapter aus (öffentliche Adresse = NCONNECT_HOST_ADDR_LE aus dem
 * N-Connect-Export), verbindet sich mit den Switch-2-Controllern, die für diese Adresse werben, und reicht den
 * rohen Eingabebericht 0x05 über USB-CDC an N-Connect weiter. Vibration/Spieler-LED kommen von N-Connect zurück.
 *
 * Rahmenformat, Adressübernahme und das Erkennen der Werbung stammen aus dem getesteten N-Connect-Code
 * (AdapterProtocol.cs, Advertisement.cs). Der GATT-Ablauf ist nach docs/PROTOKOLLE.md gebaut und muss mit echter
 * Hardware eingefahren werden – solche Stellen sind mit "PRÜFEN:" markiert.
 *
 * Stand: v0, noch nicht auf Hardware getestet.
 */

#include <zephyr/kernel.h>
#include <zephyr/sys/ring_buffer.h>
#include <zephyr/drivers/uart.h>
#include <zephyr/drivers/gpio.h>
#include <zephyr/usb/usb_device.h>
#include <zephyr/bluetooth/bluetooth.h>
#include <zephyr/bluetooth/conn.h>
#include <zephyr/bluetooth/gatt.h>
#include <zephyr/bluetooth/uuid.h>
#include <zephyr/bluetooth/controller.h>
#include <zephyr/logging/log.h>
#include <string.h>

#include "nconnect_pairing.h"   /* liefert NCONNECT_HOST_ADDR_LE[6] (niedrigstes Byte zuerst) */

LOG_MODULE_REGISTER(nconnect, LOG_LEVEL_INF);

/* ----------------------------------------------------------------------------- Protokoll (ADAPTER-PROTOKOLL.md) */

enum {
    MSG_HELLO = 0x08, MSG_CONNECTED = 0x10, MSG_DISCONNECTED = 0x11, MSG_INPUT = 0x12, MSG_LOG = 0x1F,
    MSG_SETHOST = 0x01, MSG_PING = 0x02, MSG_RUMBLE = 0x20, MSG_PLAYERLED = 0x21,
};
#define SLIP_END 0xC0
#define SLIP_ESC 0xDB
#define SLIP_ESC_END 0xDC
#define SLIP_ESC_ESC 0xDD
#define PROTOCOL_VERSION 1

static uint16_t crc16_ccitt(const uint8_t *data, size_t len)
{
    uint16_t crc = 0xFFFF;
    for (size_t i = 0; i < len; i++) {
        crc ^= (uint16_t)data[i] << 8;
        for (int b = 0; b < 8; b++)
            crc = (crc & 0x8000) ? (uint16_t)((crc << 1) ^ 0x1021) : (uint16_t)(crc << 1);
    }
    return crc;
}

/* ------------------------------------------------------------------------------------------- USB-CDC-Transport */

static const struct device *cdc_dev;
RING_BUF_DECLARE(tx_ring, 2048);
RING_BUF_DECLARE(rx_ring, 1024);

static void cdc_irq(const struct device *dev, void *user)
{
    while (uart_irq_update(dev) && uart_irq_is_pending(dev)) {
        if (uart_irq_rx_ready(dev)) {
            uint8_t buf[64];
            int n = uart_fifo_read(dev, buf, sizeof(buf));
            if (n > 0)
                ring_buf_put(&rx_ring, buf, n);
        }
        if (uart_irq_tx_ready(dev)) {
            uint8_t buf[64];
            uint32_t n = ring_buf_get(&tx_ring, buf, sizeof(buf));
            if (n > 0)
                uart_fifo_fill(dev, buf, n);
            else
                uart_irq_tx_disable(dev);
        }
    }
}

/* Einen Rahmen bauen (Art|Slot|Daten|CRC16, großes Byte zuerst) und SLIP-kodiert in den TX-Ring legen. */
static void send_frame(uint8_t type, uint8_t slot, const uint8_t *payload, size_t len)
{
    uint8_t body[256];
    if (len + 4 > sizeof(body)) return;
    body[0] = type; body[1] = slot;
    if (len) memcpy(&body[2], payload, len);
    uint16_t crc = crc16_ccitt(body, len + 2);
    body[len + 2] = (uint8_t)(crc >> 8);
    body[len + 3] = (uint8_t)crc;

    uint8_t out[520];
    size_t o = 0;
    out[o++] = SLIP_END;
    for (size_t i = 0; i < len + 4; i++) {
        uint8_t c = body[i];
        if (c == SLIP_END)      { out[o++] = SLIP_ESC; out[o++] = SLIP_ESC_END; }
        else if (c == SLIP_ESC) { out[o++] = SLIP_ESC; out[o++] = SLIP_ESC_ESC; }
        else                    { out[o++] = c; }
    }
    out[o++] = SLIP_END;
    ring_buf_put(&tx_ring, out, o);
    if (cdc_dev) uart_irq_tx_enable(cdc_dev);
}

static void adapter_log(const char *text)
{
    send_frame(MSG_LOG, 0, (const uint8_t *)text, strlen(text));
}

/* Eingehende SLIP-Rahmen zusammensetzen und verarbeiten. */
static void handle_pc_frame(const uint8_t *frame, size_t n);  /* unten */

static void rx_process(void)
{
    static uint8_t buf[300];
    static size_t len;
    static bool esc;
    uint8_t b;
    while (ring_buf_get(&rx_ring, &b, 1) == 1) {
        if (b == SLIP_END) {
            if (len >= 4) {
                uint16_t crc = (uint16_t)(buf[len - 2] << 8) | buf[len - 1];
                if (crc16_ccitt(buf, len - 2) == crc)
                    handle_pc_frame(buf, len - 2);
            }
            len = 0; esc = false;
            continue;
        }
        if (esc) { b = (b == SLIP_ESC_END) ? SLIP_END : (b == SLIP_ESC_ESC) ? SLIP_ESC : b; esc = false; }
        else if (b == SLIP_ESC) { esc = true; continue; }
        if (len < sizeof(buf)) buf[len++] = b; else len = sizeof(buf); /* Überlauf: bis zum nächsten END verwerfen */
    }
}

/* ------------------------------------------------------------------------------------- Switch-2-GATT (PROTOKOLLE) */

/* Dienst und Merkmale (128-bit, siehe docs/PROTOKOLLE.md §1.2). */
#define UUID_SVC    BT_UUID_DECLARE_128(BT_UUID_128_ENCODE(0xab7de9be,0x89fe,0x49ad,0x828f,0x118f09df7fd0))
#define UUID_INPUT  BT_UUID_DECLARE_128(BT_UUID_128_ENCODE(0xab7de9be,0x89fe,0x49ad,0x828f,0x118f09df7fd2))
#define UUID_CMD    BT_UUID_DECLARE_128(BT_UUID_128_ENCODE(0x649d4ac9,0x8eb7,0x4e6c,0xaf44,0x1ea54fe5f005))
#define UUID_RESP   BT_UUID_DECLARE_128(BT_UUID_128_ENCODE(0xc765a961,0xd9d8,0x4d36,0xa20a,0x5315b111836a))
#define UUID_VIB_PRO BT_UUID_DECLARE_128(BT_UUID_128_ENCODE(0xcc483f51,0x9258,0x427d,0xa939,0x630c31f72b05))

#define PID_PRO2 0x2069
#define PID_JOYCON2_L 0x2067
#define PID_JOYCON2_R 0x2066
#define PID_GAMECUBE2 0x2073

struct controller {
    struct bt_conn *conn;
    uint8_t slot;
    uint16_t pid;
    bool in_use;
    uint16_t input_handle;   /* Wert-Handle des Eingabeberichts */
    uint16_t input_ccc;      /* CCC-Handle */
    uint16_t cmd_handle;     /* Befehle schreiben */
    uint16_t vib_handle;     /* Vibration */
    struct bt_gatt_subscribe_params sub;
};

#define MAX_SLOTS CONFIG_BT_MAX_CONN
static struct controller slots[MAX_SLOTS];

static struct controller *slot_for_conn(struct bt_conn *conn)
{
    for (int i = 0; i < MAX_SLOTS; i++)
        if (slots[i].in_use && slots[i].conn == conn) return &slots[i];
    return NULL;
}
static struct controller *free_slot(void)
{
    for (int i = 0; i < MAX_SLOTS; i++)
        if (!slots[i].in_use) { slots[i].slot = i; return &slots[i]; }
    return NULL;
}

/* Feature-Maske je Art (docs/PROTOKOLLE §1.3): Pro/GC 0x2F, Joy-Con 2 0x37. */
static uint8_t feature_mask(uint16_t pid)
{
    return (pid == PID_JOYCON2_L || pid == PID_JOYCON2_R) ? 0x37 : 0x2F;
}

/* Befehlskopf (8 Byte): cmd|91|01(BLE)|sub|00|len|00 00, dann Daten. */
static void write_cmd(struct controller *c, uint8_t cmd, uint8_t sub, const uint8_t *data, uint8_t len)
{
    uint8_t buf[8 + 16];
    if (len > 16) return;
    buf[0] = cmd; buf[1] = 0x91; buf[2] = 0x01; buf[3] = sub; buf[4] = 0x00; buf[5] = len; buf[6] = 0; buf[7] = 0;
    if (len) memcpy(&buf[8], data, len);
    /* Schreiben ohne Antwort an das Befehlsmerkmal. */
    bt_gatt_write_without_response(c->conn, c->cmd_handle, buf, 8 + len, false);
}

/* Eingabebericht vom Controller: roh an N-Connect weiterreichen. */
static uint8_t on_input(struct bt_conn *conn, struct bt_gatt_subscribe_params *params,
                        const void *data, uint16_t length)
{
    struct controller *c = slot_for_conn(conn);
    if (!data || !c) return BT_GATT_ITER_CONTINUE;
    send_frame(MSG_INPUT, c->slot, data, length);   /* Bericht 0x05, wie über BLE, ohne Report-ID */
    return BT_GATT_ITER_CONTINUE;
}

/* PRÜFEN: Nach bt_gatt_dm die Handles der Merkmale heraussuchen und Eingaben abonnieren. */
static void start_controller(struct controller *c);

#include <bluetooth/gatt_dm.h>

static void dm_completed(struct bt_gatt_dm *dm, void *ctx)
{
    struct controller *c = ctx;
    const struct bt_gatt_dm_attr *gatt_chrc, *gatt_desc;

    gatt_chrc = bt_gatt_dm_char_by_uuid(dm, UUID_INPUT);
    if (gatt_chrc) {
        gatt_desc = bt_gatt_dm_desc_by_uuid(dm, gatt_chrc, UUID_INPUT);
        if (gatt_desc) c->input_handle = gatt_desc->handle;
        gatt_desc = bt_gatt_dm_desc_by_uuid(dm, gatt_chrc, BT_UUID_GATT_CCC);
        if (gatt_desc) c->input_ccc = gatt_desc->handle;
    }
    gatt_chrc = bt_gatt_dm_char_by_uuid(dm, UUID_CMD);
    if (gatt_chrc) { gatt_desc = bt_gatt_dm_desc_by_uuid(dm, gatt_chrc, UUID_CMD); if (gatt_desc) c->cmd_handle = gatt_desc->handle; }
    gatt_chrc = bt_gatt_dm_char_by_uuid(dm, UUID_VIB_PRO);
    if (gatt_chrc) { gatt_desc = bt_gatt_dm_desc_by_uuid(dm, gatt_chrc, UUID_VIB_PRO); if (gatt_desc) c->vib_handle = gatt_desc->handle; }

    bt_gatt_dm_data_release(dm);
    start_controller(c);
}

static void dm_not_found(struct bt_conn *conn, void *ctx) { LOG_WRN("Dienst nicht gefunden"); }
static void dm_error(struct bt_conn *conn, int err, void *ctx) { LOG_WRN("GATT-Discovery Fehler %d", err); }
static struct bt_gatt_dm_cb dm_cb = { .completed = dm_completed, .service_not_found = dm_not_found, .error_found = dm_error };

static void start_controller(struct controller *c)
{
    if (!c->input_handle || !c->cmd_handle) { LOG_WRN("Merkmale fehlen"); return; }
    uint8_t mask = feature_mask(c->pid);
    uint8_t m[4] = { mask, 0, 0, 0 };
    write_cmd(c, 0x0C, 0x02, m, 4);   /* Feature-Maske setzen */
    write_cmd(c, 0x0C, 0x04, m, 4);   /* einschalten */

    /* Eingaben abonnieren. */
    c->sub.notify = on_input;
    c->sub.value = BT_GATT_CCC_NOTIFY;
    c->sub.value_handle = c->input_handle;
    c->sub.ccc_handle = c->input_ccc;
    int err = bt_gatt_subscribe(c->conn, &c->sub);
    if (err && err != -EALREADY) { LOG_WRN("subscribe %d", err); return; }

    uint8_t pid_le[2] = { (uint8_t)c->pid, (uint8_t)(c->pid >> 8) };
    bt_addr_le_t addr; memcpy(&addr, bt_conn_get_dst(c->conn), sizeof(addr));
    uint8_t payload[8] = { pid_le[0], pid_le[1] };
    for (int i = 0; i < 6; i++) payload[2 + i] = addr.a.val[5 - i];  /* höchstes Byte zuerst für N-Connect */
    send_frame(MSG_CONNECTED, c->slot, payload, 8);
    LOG_INF("Controller verbunden, Slot %d, PID %04x", c->slot, c->pid);
}

/* --------------------------------------------------------------------------------------------- Scannen/Verbinden */

static bool match_host(struct net_buf_simple *ad, uint16_t *pid_out)
{
    while (ad->len > 1) {
        uint8_t len = net_buf_simple_pull_u8(ad);
        if (len == 0 || len > ad->len) break;
        uint8_t type = net_buf_simple_pull_u8(ad);
        if (type == BT_DATA_MANUFACTURER_DATA && len >= 1 + 2 + 16) {
            const uint8_t *m = ad->data;           /* companyId(2) + data... */
            uint16_t company = m[0] | (m[1] << 8);
            const uint8_t *d = &m[2];              /* entspricht N-Connect "data" (ohne companyId) */
            /* Nintendo 0x0553, Kennung 01 00 03 7E, PID in d[5..6], Host in d[10..15] (LE). */
            if (company == 0x0553 && d[0] == 0x01 && d[1] == 0x00 && d[2] == 0x03 && d[3] == 0x7E) {
                if (memcmp(&d[10], NCONNECT_HOST_ADDR_LE, 6) == 0) {
                    if (pid_out) *pid_out = d[5] | (d[6] << 8);
                    return true;
                }
            }
        }
        net_buf_simple_pull(ad, len - 1);
    }
    return false;
}

static struct bt_conn *pending_conn;
static uint16_t pid_of_pending;  /* PID des gerade verbundenen Controllers (bt_conn hat kein freies Feld) */

static void scan_cb(const bt_addr_le_t *addr, int8_t rssi, uint8_t type, struct net_buf_simple *ad)
{
    uint16_t pid = 0;
    if (pending_conn || !match_host(ad, &pid)) return;
    struct bt_le_conn_param *param = BT_LE_CONN_PARAM(6, 12, 0, 400);  /* ~7.5–15 ms, PRÜFEN */
    pid_of_pending = pid;
    bt_le_scan_stop();
    int err = bt_conn_le_create(addr, BT_CONN_LE_CREATE_CONN, param, &pending_conn);
    if (err) { LOG_WRN("connect create %d", err); bt_le_scan_start(BT_LE_SCAN_PASSIVE, scan_cb); }
}

static void connected(struct bt_conn *conn, uint8_t err)
{
    if (err) {
        LOG_WRN("Verbindung fehlgeschlagen (%u)", err);
        if (pending_conn == conn) { bt_conn_unref(pending_conn); pending_conn = NULL; }
        bt_le_scan_start(BT_LE_SCAN_PASSIVE, scan_cb);
        return;
    }
    struct controller *c = free_slot();
    if (!c) { bt_conn_disconnect(conn, BT_HCI_ERR_REMOTE_USER_TERM_CONN); return; }
    memset(&c->sub, 0, sizeof(c->sub));
    c->in_use = true; c->conn = bt_conn_ref(conn); c->pid = pid_of_pending;
    c->input_handle = c->input_ccc = c->cmd_handle = c->vib_handle = 0;
    pending_conn = NULL;
    /* Dienst/Merkmale suchen, dann start_controller(). */
    bt_gatt_dm_start(conn, UUID_SVC, &dm_cb, c);
    bt_le_scan_start(BT_LE_SCAN_PASSIVE, scan_cb);   /* weiter nach weiteren Controllern suchen */
}

static void disconnected(struct bt_conn *conn, uint8_t reason)
{
    struct controller *c = slot_for_conn(conn);
    if (c) {
        send_frame(MSG_DISCONNECTED, c->slot, NULL, 0);
        bt_conn_unref(c->conn);
        memset(c, 0, sizeof(*c));
    }
    bt_le_scan_start(BT_LE_SCAN_PASSIVE, scan_cb);
}

BT_CONN_CB_DEFINE(conn_cb) = { .connected = connected, .disconnected = disconnected };

/* ------------------------------------------------------------------------------------- Befehle von N-Connect */

static void handle_pc_frame(const uint8_t *frame, size_t n)
{
    uint8_t type = frame[0], slot = frame[1];
    const uint8_t *data = &frame[2];
    size_t dlen = n - 2;
    switch (type) {
    case MSG_PING: {
        uint8_t hello[1 + 16] = { PROTOCOL_VERSION };
        const char *name = "nRF52840 N-Connect";
        memcpy(&hello[1], name, strlen(name));
        send_frame(MSG_HELLO, 0, hello, 1 + strlen(name));
        break;
    }
    case MSG_SETHOST:
        /* Optional: N-Connect kann die Host-Adresse überschreiben. v0 nutzt die kompilierte NCONNECT_HOST_ADDR_LE. */
        break;
    case MSG_RUMBLE:
        if (slot < MAX_SLOTS && slots[slot].in_use && dlen >= 2 && slots[slot].vib_handle) {
            /* PRÜFEN: große/kleine Amplitude (data[0]/data[1]) in einen HD-Rumble-Frame umsetzen (PROTOKOLLE §1.5).
               v0: einfacher Dauer-Buzz skaliert mit der größeren Amplitude. */
            uint8_t amp = data[0] > data[1] ? data[0] : data[1];
            uint16_t a = (uint16_t)(amp * 453 / 255);   /* max 453 wie SDL */
            uint8_t frame33[33] = {0};
            /* hohe Freq 0x187, tiefe 0x112; 40-Bit-Frame LE (siehe PROTOKOLLE §1.5). */
            uint32_t lo = 0x187 | (a << 10);
            uint32_t hi = 0x112 | (a << 10);
            uint8_t fr[5] = { lo & 0xFF, (lo >> 8) & 0xFF, ((lo >> 16) & 0x0F) | ((hi & 0x0F) << 4),
                              (hi >> 4) & 0xFF, (hi >> 12) & 0xFF };
            frame33[0] = 0; frame33[1] = 0x50; memcpy(&frame33[2], fr, 5);
            frame33[0x11] = 0x50; memcpy(&frame33[0x12], fr, 5);
            bt_gatt_write_without_response(slots[slot].conn, slots[slot].vib_handle, frame33, sizeof(frame33), false);
        }
        break;
    case MSG_PLAYERLED:
        if (slot < MAX_SLOTS && slots[slot].in_use && dlen >= 1) {
            uint8_t d[8] = { data[0], 0,0,0, 0,0,0,0 };
            write_cmd(&slots[slot], 0x09, 0x07, d, 8);   /* Spieler-LED (PROTOKOLLE §1.3) */
        }
        break;
    }
}

/* ----------------------------------------------------------------------------------------------------- LED/main */

static const struct gpio_dt_spec led = GPIO_DT_SPEC_GET_OR(DT_ALIAS(led0), gpios, {0});

int main(void)
{
    LOG_INF("N-Connect Funkadapter startet");
    if (led.port) gpio_pin_configure_dt(&led, GPIO_OUTPUT_ACTIVE);

    /* Öffentliche Adresse = PC-Adapter-Adresse, VOR bt_enable(). */
    bt_ctlr_set_public_addr(NCONNECT_HOST_ADDR_LE);   /* PRÜFEN: bei SoftDevice-Controller ggf. HCI-VS nötig */

    int err = bt_enable(NULL);
    if (err) { LOG_ERR("bt_enable %d", err); return 0; }

    cdc_dev = DEVICE_DT_GET_ONE(zephyr_cdc_acm_uart);
    if (!device_is_ready(cdc_dev)) { LOG_ERR("CDC nicht bereit"); return 0; }
    if (usb_enable(NULL)) { LOG_ERR("usb_enable"); return 0; }
    uart_irq_callback_user_data_set(cdc_dev, cdc_irq, NULL);
    uart_irq_rx_enable(cdc_dev);

    bt_le_scan_start(BT_LE_SCAN_PASSIVE, scan_cb);

    while (1) {
        rx_process();
        if (led.port) gpio_pin_toggle_dt(&led);
        k_msleep(50);
    }
    return 0;
}

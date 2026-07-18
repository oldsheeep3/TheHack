// フラッシュ永続化 + HIDフィーチャーレポート結線(RP2350 hardware/flash依存)。
// シリアライズ/デシリアライズ/CRC検証自体はsettings_codec.c(Pico SDK非依存)に分離してあり
// (ホストテスト対象, buttons.c/buttons_debounce.cの分離パターンを踏襲)、ここではフラッシュの
// セクタ消去/書込・HIDフィーチャーレポートとの結線のみを担う。

#include "settings.h"

#include <string.h>

#include "hardware/flash.h"
#include "hardware/regs/addressmap.h"
#include "hardware/sync.h"

// フラッシュの最終セクタを設定保存専用に予約する(Pico SDK flash_nukeサンプルと同方針)。
// アプリケーションコードはフラッシュ先頭から配置されるため、末尾セクタは実行コードと
// 衝突しない。
#define SETTINGS_FLASH_OFFSET (PICO_FLASH_SIZE_BYTES - FLASH_SECTOR_SIZE)

void settings_load(settings_t *out_settings) {
    const uint8_t *flash_data = (const uint8_t *)(XIP_BASE + SETTINGS_FLASH_OFFSET);
    if (!settings_deserialize(flash_data, SETTINGS_SERIALIZED_LEN, out_settings)) {
        settings_set_defaults(out_settings);
    }
}

void settings_save(const settings_t *settings) {
    uint8_t serialized[SETTINGS_SERIALIZED_LEN];
    settings_serialize(settings, serialized);

    // flash_range_program はセクタ単位の消去後、消去済み領域(0xFF)へ書き込む必要があるため、
    // セクタサイズ分のバッファへ0xFFパディングしてから一括書込する。
    static uint8_t sector_buf[FLASH_SECTOR_SIZE];
    memset(sector_buf, 0xFF, sizeof(sector_buf));
    memcpy(sector_buf, serialized, sizeof(serialized));

    // 消去中はXIP経由の実行(フラッシュ上のコード)を止める必要があるため割込みを無効化する。
    // 設定保存はPCからの投入時のみに限られる低頻度操作であり、数ms程度のブロッキングは
    // 許容する(継続的なSTATE集約/バックライト配布とは異なる経路のため非機能要件と抵触しない)。
    uint32_t saved_irq = save_and_disable_interrupts();
    flash_range_erase(SETTINGS_FLASH_OFFSET, FLASH_SECTOR_SIZE);
    flash_range_program(SETTINGS_FLASH_OFFSET, sector_buf, sizeof(sector_buf));
    restore_interrupts(saved_irq);
}

void settings_on_feature_report(const uint8_t *data, uint16_t len) {
    settings_t parsed;
    if (settings_deserialize(data, len, &parsed)) {
        settings_save(&parsed);
    }
    // 検証失敗時は無視する(不正な設定でフラッシュを上書きしない)。
}

uint16_t settings_build_feature_report(uint8_t *out, uint16_t out_capacity) {
    if (out_capacity < SETTINGS_SERIALIZED_LEN) {
        return 0;
    }

    settings_t current;
    settings_load(&current);
    settings_serialize(&current, out);
    return SETTINGS_SERIALIZED_LEN;
}

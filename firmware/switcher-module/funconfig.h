#ifndef _FUNCONFIG_H
#define _FUNCONFIG_H

// ch32v003fun (ch32fun.h) が最初にincludeする設定ヘッダ。プロジェクト直下に置くことが
// ch32fun の規約で必須 (examples/*/funconfig.h と同じ位置づけ)。
// MCUの選択自体は Makefile の TARGET_MCU (CH32V003) で行うため、ここでは既定値から
// 変更したい項目だけを定義する。設定可能な項目の一覧は ch32v003fun/ch32fun/ch32fun.h 参照。

// printfはこのファームでは使わない。デバッグ時にSWIO経由の printf が欲しくなったら
// FUNCONF_USE_DEBUGPRINTF を 1 にする (既定1のため、ここで明示的に切っておく)。
// SRAM 2KB / Flash 16KB の節約が目的。
#define FUNCONF_USE_DEBUGPRINTF 0
#define FUNCONF_USE_UARTPRINTF 0

#endif

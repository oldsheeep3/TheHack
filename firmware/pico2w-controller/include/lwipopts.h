#ifndef PICO2W_CONTROLLER_LWIPOPTS_H
#define PICO2W_CONTROLLER_LWIPOPTS_H

// pico-examples の pico_w/wifi 系サンプルにならった NO_SYS=1 構成の最小 lwipopts.h。
// wireless.c は UDP raw API (udp_new/udp_bind/udp_recv/udp_sendto) のみを使い、
// ソケット/TCPは不要なため無効化してコードサイズ・依存を抑える
// (ENABLE_WIRELESS 有効ビルドでのみ pico_cyw43_arch_lwip_threadsafe_background 経由で使用)。

#define NO_SYS 1
#define LWIP_SOCKET 0
#define LWIP_NETCONN 0

#define MEM_ALIGNMENT 4
#define MEM_SIZE 4000

#define LWIP_ARP 1
#define LWIP_ETHERNET 1
#define LWIP_ICMP 1
#define LWIP_RAW 0

#define LWIP_UDP 1
#define LWIP_TCP 0
#define LWIP_DHCP 1
#define LWIP_DNS 0
#define LWIP_IPV4 1
#define LWIP_IPV6 0

#define LWIP_NETIF_STATUS_CALLBACK 1
#define LWIP_NETIF_LINK_CALLBACK 1

#define TCPIP_THREAD_STACKSIZE 1024
#define DEFAULT_THREAD_STACKSIZE 1024
#define DEFAULT_RAW_RECVMBOX_SIZE 8
#define TCPIP_MBOX_SIZE 8

#define PBUF_POOL_SIZE 8

#define LWIP_DEBUG 0
#define LWIP_STATS 0
#define LWIP_STATS_DISPLAY 0

#endif // PICO2W_CONTROLLER_LWIPOPTS_H

# N-Connect-Funkadapter (nRF52840-Dongle) – nRF Connect SDK / Zephyr
cmake_minimum_required(VERSION 3.20.0)
find_package(Zephyr REQUIRED HINTS $ENV{ZEPHYR_BASE})
project(nconnect_adapter)

target_sources(app PRIVATE src/main.c)
# nconnect_pairing.h (aus dem N-Connect-Export) liegt neben dieser Datei.
target_include_directories(app PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

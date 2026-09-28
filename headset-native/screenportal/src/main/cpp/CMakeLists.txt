cmake_minimum_required(VERSION 3.22.1)
project(screenportal CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(screenportal SHARED
    jni_bridge.cpp
    stream_session.cpp
    reassembler.cpp
    clock_sync.cpp)

target_compile_options(screenportal PRIVATE -Wall -Wextra -fno-exceptions -fno-rtti)
# 16 KB page size (exigido para apps novos na Meta Store / Android 15+).
target_link_options(screenportal PRIVATE "-Wl,-z,max-page-size=16384")

target_link_libraries(screenportal
    mediandk
    android
    log)

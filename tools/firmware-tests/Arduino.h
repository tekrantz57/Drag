#pragma once

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

using std::min;
using PGM_P = const char*;
#define PSTR(value) value
#define strcmp_P strcmp
#define strncmp_P strncmp
#define strncpy_P strncpy
#define snprintf_P snprintf
#ifdef _MSC_VER
#define strtok_r strtok_s
#endif
constexpr int HIGH = 1, LOW = 0, INPUT = 0, INPUT_PULLUP = 2, OUTPUT = 1;
constexpr uint8_t A0 = 54, A1 = 55, A2 = 56, A3 = 57;
constexpr uint8_t A4 = 58, A5 = 59, A6 = 60, A7 = 61;
constexpr uint8_t A8 = 62, A9 = 63, A10 = 64, A11 = 65;
constexpr uint8_t A12 = 66, A13 = 67, A14 = 68, A15 = 69;
inline unsigned long testTimeMs = 0;
inline unsigned long millis() { return testTimeMs; }
inline unsigned long micros() { return testTimeMs * 1000UL; }
inline void pinMode(uint8_t, int) {}
inline void digitalWrite(uint8_t, int) {}
inline int testPinLevels[256] = {};
inline int digitalRead(uint8_t pin) { return testPinLevels[pin]; }

struct TestSerial {
  std::string output;
  int writeQuota = 1;
  void begin(int) {}
  int available() { return 0; }
  int read() { return -1; }
  int availableForWrite() { return writeQuota; }
  void write(const uint8_t* bytes, size_t length) {
    output.append(reinterpret_cast<const char*>(bytes), length);
  }
};
inline TestSerial Serial;

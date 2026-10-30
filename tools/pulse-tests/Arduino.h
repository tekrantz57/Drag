#pragma once
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <sstream>
#include <string>

#define F(value) value
#define IRAM_ATTR
using portMUX_TYPE = int;
constexpr int portMUX_INITIALIZER_UNLOCKED = 0;
inline void portENTER_CRITICAL(portMUX_TYPE* mux) { ++*mux; }
inline void portEXIT_CRITICAL(portMUX_TYPE* mux) { --*mux; }
inline void portENTER_CRITICAL_ISR(portMUX_TYPE* mux) { ++*mux; }
inline void portEXIT_CRITICAL_ISR(portMUX_TYPE* mux) { --*mux; }
constexpr int HIGH = 1, LOW = 0, OUTPUT = 1;
inline uint32_t testClock = 0;
inline int testLevels[64] = {};
inline void (*testTimeHook)() = nullptr;
inline uint32_t micros() {
  if (testTimeHook) testTimeHook();
  return testClock;
}
inline void digitalWrite(uint8_t pin, int level) { testLevels[pin] = level; }
inline void pinMode(uint8_t, int) {}
struct SerialMock {
  std::string input, output;
  void begin(int) {}
  void flush() {}
  int available() { return static_cast<int>(input.size()); }
  int read() {
    char result = input.front();
    input.erase(0, 1);
    return result;
  }
  template <class T> void print(T value) {
    std::ostringstream stream;
    stream << value;
    output += stream.str();
  }
  void println() { output += '\n'; }
  template <class T> void println(T value) { print(value); println(); }
};
inline SerialMock Serial;

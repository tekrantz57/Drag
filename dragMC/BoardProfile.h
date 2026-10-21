#pragma once

// Both targets use the Programming USB port (Serial). Never use a Mega image on a Due.
#if defined(ARDUINO_SAM_DUE) || defined(DRAG_TEST_DUE)
#define DRAG_DUE_CAPTURE 1
#elif defined(ARDUINO) && !defined(__AVR_ATmega2560__)
#error "DragMC supports only the Mega 2560 and experimental Arduino Due."
#endif

namespace DragBoard {

#ifdef DRAG_DUE_CAPTURE
constexpr char Product[] = "DRAG_MC_DUE";
constexpr char Version[] = DRAGMC_DUE_FIRMWARE_VERSION;
constexpr char Mcu[] = "SAM3X8E";
#else
constexpr char Product[] = "DRAG_MC";
constexpr char Version[] = DRAGMC_FIRMWARE_VERSION;
constexpr char Mcu[] = "MEGA2560";
#endif

constexpr uint8_t LIGHT_PINS[4][7] = {
    {22, 23, 24, 25, 26, 27, 28},
    {29, 30, 31, 32, 33, 34, 35},
    {36, 37, 38, 39, 40, 41, 42},
    {43, 44, 45, 46, 47, 48, 49}
};

// Pre-stage, stage, optional interval 1, optional interval 2, trap, finish.
constexpr uint8_t SENSOR_PINS[4][6] = {
    {A0, A1, 2, 3, A2, A3},
    {A4, A5, 4, 5, A6, A7},
    {A8, A9, 6, 7, A10, A11},
#ifdef DRAG_DUE_CAPTURE
    {50, 51, 8, 9, 52, 53}
#else
    {A12, A13, 8, 9, A14, A15}
#endif
};

#if __cplusplus >= 201402L
constexpr
#else
inline
#endif
bool validPinMap() {
  for (uint8_t lane = 0; lane < 4; ++lane) {
    for (uint8_t input = 0; input < 6; ++input) {
      const uint8_t pin = SENSOR_PINS[lane][input];
      if (pin < 2) return false;  // Reserve the Programming-port UART pins.
      for (uint8_t otherLane = 0; otherLane < 4; ++otherLane) {
        for (uint8_t light = 0; light < 7; ++light)
          if (pin == LIGHT_PINS[otherLane][light]) return false;
        for (uint8_t otherInput = 0; otherInput < 6; ++otherInput)
          if ((lane != otherLane || input != otherInput) &&
              pin == SENSOR_PINS[otherLane][otherInput]) return false;
      }
    }
  }
  return true;
}

// C++11 AVR builds validate this in host tests instead (constexpr loops need C++14).
#if __cplusplus >= 201402L
static_assert(validPinMap(), "Sensor pins must be unique and separate from Tree/UART pins");
#endif
}  // namespace DragBoard

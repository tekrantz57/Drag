#include <Arduino.h>
#include <stdlib.h>
#include <string.h>
#include "PulseTrain.h"
#ifdef CONFIG_IDF_TARGET_ESP32C3
#include "C3RmtOutput.h"
#endif

#if !defined(__AVR_ATmega2560__) && !defined(ARDUINO_SAM_DUE) && !defined(CONFIG_IDF_TARGET_ESP32C3)
#error "Select Mega 2560, Due (Programming Port), or ESP32C3 Dev Module."
#endif

namespace {
#ifdef CONFIG_IDF_TARGET_ESP32C3
// Avoid boot strapping, onboard LED/BOOT, flash, UART and native USB pins.
constexpr uint8_t PINS[4] = {4, 5, 6, 7};
bool npnInterface = false;
#else
constexpr uint8_t PINS[4] = {22, 23, 24, 25};
bool npnInterface = true;
#endif

int outputLevel(bool blocked) {
  return blocked != npnInterface ? HIGH : LOW;
}

struct Output {
  uint32_t now() { return micros(); }
  void set(uint8_t channel, bool blocked) {
    // NPN mode accounts for the external transistor's inversion.
    digitalWrite(PINS[channel], outputLevel(blocked));
  }
} output;

DragPulse::Train<Output> train(output);
#ifdef CONFIG_IDF_TARGET_ESP32C3
C3RmtOutput<Output> rmtOutput(output, PINS);
DragPulse::RmtTrain<C3RmtOutput<Output>> hardwareTrain(rmtOutput);
bool hardwareEngine = true;
#endif
DragPulse::Config config = {};
char line[96];
uint8_t used = 0;
bool overflow = false;

bool isRunning() {
#ifdef CONFIG_IDF_TARGET_ESP32C3
  if (hardwareEngine) return hardwareTrain.running;
#endif
  return train.running;
}

void stopTrain() {
#ifdef CONFIG_IDF_TARGET_ESP32C3
  if (hardwareEngine) {
    hardwareTrain.stop();
    return;
  }
#endif
  train.stop();
}

void help() {
  Serial.println(F("DRAG_PULSE_TEST 0.3.1 - standalone, NOT DragMC"));
#ifdef CONFIG_IDF_TARGET_ESP32C3
  Serial.println(F("ESP32-C3: GPIO4/5/6/7 = channels 1/2/3/4; outputs are 3.3 V."));
  Serial.print(F("Current engine: "));
  Serial.println(hardwareEngine ? F("HARDWARE RMT (one selected channel)") : F("SOFTWARE"));
  Serial.println(F("ENGINE HARDWARE|SOFTWARE: idle only. C3 defaults to HARDWARE."));
  Serial.println(F("Hardware RUN mask must be 1, 2, 4 or 8; software supports 1..15."));
  Serial.println(F("RMT times blocked pulses; clear gaps can grow with task scheduling."));
#else
  Serial.println(F("Mega/Due: D22/D23/D24/D25 = channels 1/2/3/4."));
#endif
  Serial.print(F("Current interface: "));
  Serial.println(npnInterface ? F("NPN (inverted)") : F("DIRECT (active HIGH)"));
  Serial.println(F("MODE DIRECT|NPN: idle only; match wiring, recheck voltage, reset receiver counts."));
  Serial.println(F("DIRECT: never send 5 V to Due/ESP32; see wiring guide before connecting."));
  Serial.println(F("115200 baud, newline. Units are microseconds (1000 us = 1 ms)."));
  Serial.println(F("RUN width_us gap_us count jitter_us mask"));
  Serial.println(F("width/gap: 100..1000000; count: 1..1000000; jitter: 0..1000000"));
  Serial.println(F("mask: 1..15 (bits select channels); e.g. 1=ch1, 15=all4"));
  Serial.println(F("Example: RUN 2000 20000 1000 997 1"));
  Serial.println(F("LEVEL mask 0|1: hold clear|blocked for meter checks (idle only)."));
  Serial.println(F("STOP: clear ALL outputs and abort. HELP: this text (idle only)."));
  Serial.println(F("No console output during a run; only STOP accepted. Reset clears settings."));
}

bool number(const char* token, uint32_t& value) {
  if (!token || !*token) return false;
  value = 0;
  while (*token) {
    if (*token < '0' || *token > '9') return false;
    const uint8_t digit = *token++ - '0';
    if (value > (UINT32_MAX - digit) / 10) return false;
    value = value * 10 + digit;
  }
  return true;
}

void range(const DragPulse::Range& r) {
  if (r.minimum == UINT32_MAX) Serial.print(F("n/a"));
  else {
    Serial.print(r.minimum);
    Serial.print('-');
    Serial.print(r.maximum);
  }
}

void report(bool aborted) {
#ifdef CONFIG_IDF_TARGET_ESP32C3
  if (hardwareEngine) {
    Serial.print(hardwareTrain.failed ? F("ERROR RMT") : (aborted ? F("ABORTED") : F("DONE")));
    Serial.print(F(" completed_pulses="));
    Serial.print(hardwareTrain.completed);
    Serial.print(F(" engine=HARDWARE_RMT programmed_width_us="));
    Serial.println(config.widthUs);
    Serial.print(F("requested_clear_gap_us="));
    range(hardwareTrain.requestedGaps);
    Serial.print(F(" software_rearm_wait_us="));
    range(hardwareTrain.rearmWait);
    Serial.print(F(" max_rearm_lateness_us="));
    Serial.println(hardwareTrain.maxLateUs);
    Serial.println(F("Width is hardware-programmed, NOT measured at the wire. Gaps may be longer."));
    if (aborted || hardwareTrain.failed)
      Serial.println(F("Count includes confirmed completions only; a partial/unconfirmed pulse may exist."));
    return;
  }
#endif
  Serial.print(aborted ? F("ABORTED") : F("DONE"));
  Serial.print(F(" completed_pulses_per_selected_channel="));
  Serial.print(train.completed);
  Serial.print(F(" max_scheduler_lateness_us="));
  Serial.println(train.maxLateUs);
  for (uint8_t i = 0; i < 4; ++i) {
    if (!(config.mask & (1U << i))) continue;
    Serial.print(F("CH"));
    Serial.print(i + 1);
    Serial.print(F(" software_width_us="));
    range(train.widths[i]);
    Serial.print(F(" software_gap_us="));
    range(train.gaps[i]);
    Serial.println();
  }
  Serial.println(F("Software timestamps are approximate, NOT calibrated wire measurements."));
  if (aborted) Serial.println(F("An interrupted partial pulse is NOT included in completed count."));
}

void command() {
  char* tokens[7];
  uint8_t count = 0;
  char* token = strtok(line, " \t");
  while (token && count < 7) {
    tokens[count++] = token;
    token = strtok(nullptr, " \t");
  }
  if (!count) return;
  if (count == 1 && strcmp(tokens[0], "STOP") == 0) {
    const bool active = isRunning();
    stopTrain();
    if (active) report(true);
    else Serial.println(F("STOPPED: all channels clear."));
    return;
  }
  if (isRunning()) return;
  if (count == 1 && strcmp(tokens[0], "HELP") == 0) {
    help();
    return;
  }
#ifdef CONFIG_IDF_TARGET_ESP32C3
  if (count == 2 && strcmp(tokens[0], "ENGINE") == 0 &&
      (strcmp(tokens[1], "HARDWARE") == 0 || strcmp(tokens[1], "SOFTWARE") == 0)) {
    stopTrain();
    hardwareEngine = strcmp(tokens[1], "HARDWARE") == 0;
    Serial.println(hardwareEngine ? F("ENGINE HARDWARE: one channel, hardware-timed pulse width.")
                                  : F("ENGINE SOFTWARE: original polling-based generator."));
    return;
  }
#endif
  if (count == 2 && strcmp(tokens[0], "MODE") == 0 &&
      (strcmp(tokens[1], "DIRECT") == 0 || strcmp(tokens[1], "NPN") == 0)) {
    npnInterface = strcmp(tokens[1], "NPN") == 0;
    stopTrain();
    Serial.println(npnInterface ? F("MODE NPN: idle HIGH at generator, LOW at receiver.")
                                : F("MODE DIRECT: idle LOW, pulses HIGH at generator."));
    Serial.println(F("Mode change can create an edge. Recheck levels; reset receiver counts."));
    return;
  }
  uint32_t values[5];
  if (count == 6 && strcmp(tokens[0], "RUN") == 0) {
    for (uint8_t i = 0; i < 5; ++i) {
      if (!number(tokens[i + 1], values[i])) {
        Serial.println(F("ERROR: RUN requires five unsigned decimal integers."));
        return;
      }
    }
    DragPulse::Config next = {values[0], values[1], values[2], values[3], 0};
    if (values[4] <= 15) next.mask = static_cast<uint8_t>(values[4]);
    if (!DragPulse::valid(next)) {
      Serial.println(F("ERROR: RUN out of range; HELP lists limits."));
      return;
    }
#ifdef CONFIG_IDF_TARGET_ESP32C3
    if (hardwareEngine && !DragPulse::singleChannel(next.mask)) {
      Serial.println(F("ERROR: HARDWARE requires mask 1, 2, 4 or 8. Use ENGINE SOFTWARE for multiple outputs."));
      return;
    }
#endif
    stopTrain();
    Serial.print(F("CONFIG width_us="));
    Serial.print(next.widthUs);
    Serial.print(F(" gap_us="));
    Serial.print(next.gapUs);
    Serial.print(F(" count="));
    Serial.print(next.count);
    Serial.print(F(" jitter_us="));
    Serial.print(next.jitterUs);
    Serial.print(F(" mask="));
    Serial.println(static_cast<unsigned int>(next.mask));
    Serial.print(F("INTERFACE "));
    Serial.println(npnInterface ? F("NPN") : F("DIRECT"));
#ifdef CONFIG_IDF_TARGET_ESP32C3
    Serial.print(F("ENGINE "));
    Serial.println(hardwareEngine ? F("HARDWARE_RMT") : F("SOFTWARE"));
#endif
    Serial.println(F("START: outputs clear; first pulse in 1 second after console flush."));
    Serial.flush();
    config = next;
#ifdef CONFIG_IDF_TARGET_ESP32C3
    if (hardwareEngine) {
      if (!hardwareTrain.begin(config, npnInterface))
        Serial.println(F("ERROR: RMT initialization failed; no run started. Outputs cleared."));
      return;
    }
#endif
    train.begin(config);
    return;
  }
  if (count == 3 && strcmp(tokens[0], "LEVEL") == 0 &&
      number(tokens[1], values[0]) && number(tokens[2], values[1]) &&
      values[0] >= 1 && values[0] <= 15 && values[1] <= 1) {
    stopTrain();
    for (uint8_t i = 0; i < 4; ++i)
      if (values[0] & (1U << i)) output.set(i, values[1] == 1);
    Serial.println(F("LEVEL set; STOP restores all channels clear."));
    return;
  }
  Serial.println(F("ERROR: unknown or malformed command; send HELP."));
}
}  // namespace

void setup() {
  for (uint8_t i = 0; i < 4; ++i) {
    digitalWrite(PINS[i], outputLevel(false));
    pinMode(PINS[i], OUTPUT);
  }
  Serial.begin(115200);
  help();
}

void loop() {
  const bool wasRunning = isRunning();
#ifdef CONFIG_IDF_TARGET_ESP32C3
  if (hardwareEngine) hardwareTrain.tick();
  else train.tick();
#else
  train.tick();
#endif
  if (wasRunning && !isRunning()) report(false);
  // Bounded parsing keeps incoming serial traffic from starving generation.
  for (uint8_t budget = 0; budget < 8 && Serial.available(); ++budget) {
    const char ch = static_cast<char>(Serial.read());
    if (ch == '\r') continue;
    if (ch == '\n') {
      if (!overflow) {
        line[used] = '\0';
        command();
      } else if (!isRunning()) Serial.println(F("ERROR: line too long."));
      used = 0;
      overflow = false;
    } else if (used < sizeof(line) - 1) line[used++] = ch;
    else overflow = true;
  }
}

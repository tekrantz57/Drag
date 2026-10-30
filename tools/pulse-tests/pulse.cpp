#include <cassert>
#include <iostream>
#include <vector>
#include "../../dragPulseTest/dragPulseTest.ino"
#include "../../dragPulseTest/RmtPulseTrain.h"

struct FakeIO {
  uint32_t time = 0;
  bool levels[4] = {};
  uint32_t now() { return time; }
  void set(uint8_t channel, bool blocked) { levels[channel] = blocked; }
};

struct FakeHardware : FakeIO {
  DragPulse::PulsePlan plan;
  bool initFails = false, fireFails = false, active = false, stuck = false;
  uint8_t selected = 0;
  uint32_t endAt = 0, sent = 0;
  bool prepare(const DragPulse::PulsePlan& p, uint8_t channel) {
    plan = p;
    selected = channel;
    return !initFails;
  }
  bool fire() {
    if (fireFails) return false;
    uint32_t duration = 0;
    for (uint8_t i = 0; i < plan.length; ++i) duration += plan.halves[i].ticks;
    endAt = time + duration;
    active = true;
    ++sent;
    return true;
  }
  bool done() {
    if (!stuck && static_cast<int32_t>(time - endAt) >= 0) active = false;
    return !active;
  }
  void release() { active = false; }
};

void hardwareTests() {
  // Check every allowed width: no zero-duration halves or mid-pulse clear,
  // and the entire pulse fits below the RMT refill threshold.
  for (uint32_t width = 100; width <= 1000000; ++width) {
    DragPulse::PulsePlan p;
    assert(p.build(width, false));
    assert(!(p.length & 1) && p.length / 2 < 24);
    uint32_t blockedTicks = 0, clearTicks = 0;
    for (uint8_t i = 0; i < p.length; ++i) {
      assert(p.halves[i].ticks && p.halves[i].ticks <= 32767);
      if (p.halves[i].high) {
        assert(!clearTicks);
        blockedTicks += p.halves[i].ticks;
      } else clearTicks += p.halves[i].ticks;
    }
    assert(blockedTicks == width && clearTicks == 2);
  }
  DragPulse::PulsePlan inverted;
  assert(inverted.build(1400, true));
  assert(inverted.idleHigh && !inverted.halves[0].high && inverted.halves[1].high);
  assert(!inverted.build(99, true) && !inverted.build(1000001, true));
  FakeHardware io;
  DragPulse::RmtTrain<FakeHardware> t(io);
  assert(!t.begin({1400, 20000, 1000, 997, 15}, true));
  io.time = 0xFFF80000UL;
  assert(t.begin({1400, 20000, 1000, 997, 8}, true));
  assert(io.selected == 3);
  for (uint32_t steps = 0; t.running && steps < 100000; ++steps) {
    io.time += 5000;  // Deliberately observe every pulse after hardware ends it.
    t.tick();
  }
  assert(!t.running && !t.failed && t.completed == 1000 && io.sent == 1000);
  assert(t.rearmWait.minimum >= 20000 && t.maxLateUs <= 4999);
  io.initFails = true;
  assert(!t.begin({100, 100, 1, 0, 1}, false) && t.failed && !t.running);
  io.initFails = false;
  assert(t.begin({100, 100, 1, 0, 1}, false));
  io.fireFails = true;
  io.time += 1000000;
  t.tick();
  assert(t.failed && !t.running && !t.completed);
  io.fireFails = false;
  assert(t.begin({100, 100, 1, 0, 1}, false));
  io.time += 1000000;
  t.tick();
  t.stop();
  assert(!t.running && !t.completed && !io.active);
  assert(t.begin({100, 100, 1, 0, 1}, false));
  io.time += 1000000;
  t.tick();
  io.time += 102;
  t.stop();
  assert(t.completed == 1);
  assert(t.begin({100, 100, 1, 0, 1}, false));
  io.time += 1000000;
  t.tick();
  io.stuck = true;
  io.time += 2000101;
  t.tick();
  assert(t.failed && !t.running && !t.completed);
}

void send(const std::string& text) {
  Serial.input += text + "\n";
  while (Serial.available()) loop();
}

std::vector<uint32_t> exercise(uint32_t start, uint8_t mask) {
  FakeIO io;
  io.time = start;
  DragPulse::Train<FakeIO> t(io);
  DragPulse::Config c = {500, 1000, 100, 997, mask};
  assert(t.begin(c));
  assert(!t.begin(c));
  uint32_t blocks = 0;
  for (uint32_t elapsed = 0; t.running && elapsed < 2000000; elapsed += 13) {
    const bool before = t.blocked;
    io.time = start + elapsed;
    t.tick();
    if (!before && t.blocked) ++blocks;
    for (uint8_t channel = 0; channel < 4; ++channel)
      assert(io.levels[channel] == (t.blocked && (mask & (1U << channel))));
  }
  assert(!t.running && t.completed == 100 && blocks == 100);
  assert(t.maxLateUs <= 12);
  for (uint8_t channel = 0; channel < 4; ++channel) {
    if (mask & (1U << channel)) {
      assert(t.widths[channel].minimum >= 500 && t.widths[channel].maximum <= 512);
      assert(t.gaps[channel].minimum >= 1000 && t.gaps[channel].maximum <= 2009);
      assert(t.gaps[channel].maximum > t.gaps[channel].minimum);
    } else assert(t.widths[channel].minimum == UINT32_MAX);
  }
  return {t.widths[0].minimum, t.widths[0].maximum,
          t.gaps[0].minimum, t.gaps[0].maximum};
}

int main() {
  hardwareTests();
  assert(exercise(0, 1) == exercise(0xFFF80000UL, 1));
  exercise(0, 15);
  FakeIO io;
  DragPulse::Train<FakeIO> t(io);
  DragPulse::Config c = {100, 100, 1, 0, 1};
  assert(t.begin(c));
  io.time = 1000000;
  t.tick();
  assert(t.blocked && !t.completed);
  io.time += 5000;
  t.tick();
  assert(!t.running && t.completed == 1);
  assert(t.widths[0].maximum == 5000 && t.maxLateUs == 4900);
  assert(t.begin(c));
  io.time += 1000000;
  t.tick();
  t.stop();
  assert(!t.running && !t.blocked && !t.completed && !io.levels[0]);
  assert(!t.begin({99, 100, 1, 0, 1}));
  assert(!t.begin({100, 99, 1, 0, 1}));
  assert(!t.begin({100, 100, 0, 0, 1}));
  assert(!t.begin({100, 100, 1, 1000001, 1}));
  assert(!t.begin({100, 100, 1, 0, 16}));
  assert(!t.begin({1000001, 100, 1, 0, 1}));
  setup();
#ifdef CONFIG_IDF_TARGET_ESP32C3
  assert(!npnInterface && PINS[0] == 4 && PINS[3] == 7);
  assert(hardwareEngine);
  send("ENGINE SOFTWARE");
  assert(!hardwareEngine);
#else
  assert(npnInterface && PINS[0] == 22 && PINS[3] == 25);
#endif
  for (auto pin : PINS) assert(testLevels[pin] == outputLevel(false));
  send("MODE DIRECT");
  assert(!npnInterface);
  for (auto pin : PINS) assert(testLevels[pin] == LOW);
  send("LEVEL 1 1");
  assert(testLevels[PINS[0]] == HIGH && testLevels[PINS[1]] == LOW);
  send("STOP");
  assert(testLevels[PINS[0]] == LOW);
  send("RUN 1000 1000 1 0 1");
  testClock += 1000000;
  loop();
  assert(testLevels[PINS[0]] == HIGH);
  testClock += 1000;
  loop();
  assert(!train.running && train.completed == 1 && testLevels[PINS[0]] == LOW);
  send("MODE NPN");
  assert(npnInterface);
  for (auto pin : PINS) assert(testLevels[pin] == HIGH);
  send("MODE INVALID");
  assert(npnInterface);
  send("LEVEL 1 1");
  assert(testLevels[PINS[0]] == LOW && testLevels[PINS[1]] == HIGH);
  send("STOP");
  assert(testLevels[PINS[0]] == HIGH);
  send("RUN 1000 1000 1 0 256");
  assert(!train.running);
  send("RUN 4294967296 1000 1 0 1");
  assert(!train.running);
  send("RUN -1 1000 1 0 1");
  assert(!train.running);
  send("RUN 1000 1000 1 0 1 extra");
  assert(!train.running);
  send(std::string(110, 'X'));
  assert(Serial.output.find("line too long") != std::string::npos);
  send("RUN 1000 1000 1 0 1");
  assert(train.running);
  Serial.output.clear();
  send("HELP");
  send("LEVEL 1 1");
  send("MODE DIRECT");
  assert(npnInterface);
  assert(Serial.output.empty());
  testClock += 1000000;
  loop();
  assert(testLevels[PINS[0]] == LOW);
  testClock += 1000;
  loop();
  assert(!train.running && train.completed == 1 && testLevels[PINS[0]] == HIGH);
  assert(Serial.output.find("DONE completed_pulses_per_selected_channel=1") != std::string::npos);
  send("RUN 1000 1000 2 0 15");
  testClock += 1000000;
  loop();
  send("STOP");
  assert(!train.running && train.completed == 0);
  for (auto pin : PINS) assert(testLevels[pin] == HIGH);
  assert(Serial.output.find("ABORTED") != std::string::npos);
#ifdef CONFIG_IDF_TARGET_ESP32C3
  send("ENGINE HARDWARE");
  assert(hardwareEngine);
  send("RUN 1400 20000 2 997 15");
  assert(!isRunning());
  send("RUN 1400 20000 1 997 1");
  assert(isRunning());
  send("ENGINE SOFTWARE");
  assert(hardwareEngine);
  testClock += 1000000;
  loop();
  assert(testLevels[PINS[0]] == LOW);
  // Busy polls must remain silent and must not count a previous callback.
  const auto outputBeforePolls = Serial.output;
  for (int i = 0; i < 20; ++i) loop();
  assert(isRunning() && hardwareTrain.completed == 0 && Serial.output == outputBeforePolls);
  testClock += 10000;  // Software late, but driver confirms a completed pulse.
  loop();
  assert(!isRunning() && hardwareTrain.completed == 1 && testLevels[PINS[0]] == HIGH);
  assert(Serial.output.find("engine=HARDWARE_RMT programmed_width_us=1400") != std::string::npos);
  send("LEVEL 1 1");
  assert(testLevels[PINS[0]] == LOW);
  send("STOP");
  assert(testLevels[PINS[0]] == HIGH);
  mockRmtInitFails = true;
  send("RUN 1400 20000 1 997 1");
  assert(!isRunning() && hardwareTrain.failed && testLevels[PINS[0]] == HIGH);
  mockRmtInitFails = false;
  mockRmtTransmitFails = true;
  send("RUN 1400 20000 1 997 1");
  testClock += 1000000;
  loop();
  assert(!isRunning() && hardwareTrain.failed && !hardwareTrain.completed);
  mockRmtTransmitFails = false;
  mockRmtCallbackFails = true;
  send("RUN 1400 20000 1 997 1");
  assert(!isRunning() && hardwareTrain.failed);
  mockRmtCallbackFails = false;
  mockRmtImmediateComplete = true;
  send("RUN 100 100 2 0 1");
  testClock += 1000000;
  loop();
  loop();
  assert(isRunning() && hardwareTrain.completed == 1);
  testClock += 100;
  loop();
  loop();
  assert(!isRunning() && hardwareTrain.completed == 2);
  mockRmtImmediateComplete = false;
#endif
  std::cout << "Pulse generator software/RMT planning, timing, failure, wrap, parser and abort tests passed.\n";
}

#include <cassert>
#include <iostream>
#include "../../dragMC/dragMC.ino"

void resetTest(RaceMode mode) {
  Serial.output.clear();
  Serial.writeQuota = 1;
  testTimeMs = 0;
  serialQueueCount = serialQueueHead = serialQueueTail = serialHeadOffset = 0;
  droppedSerialMessageCount = 0;
  raceMode = mode;
  starterButtonMode = startAuthorized = false;
  lightTestEnabled = false;
  stagedDelayMs = DEFAULT_STAGED_DELAY_MS;
  activeLaneCount = 4;
  heatLaneMask = splitLaneMask = 0x0F;
  enterState(TreeState::WaitingForAllLanes, 0);
  serialQueueCount = serialQueueHead = serialQueueTail = 0;
}

size_t count(const std::string& text, const std::string& value) {
  size_t matches = 0, at = 0;
  while ((at = text.find(value, at)) != std::string::npos) {
    ++matches;
    at += value.size();
  }
  return matches;
}

void drain() {
  for (int iteration = 0; iteration < 100000; ++iteration) {
    serviceRaceResults();
    serviceSerialOutput();
    assert(serialQueueCount <= SERIAL_QUEUE_CAPACITY);
    if (!raceResultsPending && serialQueueCount == 0) return;
  }
  assert(false && "Result delivery did not finish");
}

void command(const std::string& payload) {
  char frame[128];
  snprintf(frame, sizeof(frame), "%s:%02X", payload.c_str(), calculateChecksum(payload.c_str()));
  processCommand(frame);
  drain();
}

void stage(bool blocked) {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    for (uint8_t sensor = 0; sensor < SENSORS_PER_LANE; ++sensor) {
      testPinLevels[SENSOR_PINS[lane][sensor]] =
          blocked && (sensor == PreStageSensor || sensor == StageSensor) ? HIGH : LOW;
      sensors[lane][sensor] = DebouncedBeamSensor{};
      sensors[lane][sensor].begin(SENSOR_PINS[lane][sensor]);
    }
  }
}

void starterTests() {
  for (auto mode : {RaceMode::HeadsUp, RaceMode::Bracket}) {
    for (auto tree : {TreeMode::Full, TreeMode::Pro}) {
      resetTest(mode);
      treeMode = tree;
      heatLaneMask = 1;
      stage(false);
      command("ARM:MANUAL");
      command("START_TREE:1");
      assert(!startAuthorized && treeState == TreeState::WaitingForAllLanes);
      stage(true);
      updateTree(0);
      const auto token = stagingToken;
      testTimeMs = 10000;
      updateTree(testTimeMs);
      assert(treeState == TreeState::StagingHold && !startAuthorized);
      command("START_TREE:" + std::to_string(token));
      assert(startAuthorized);
      const auto authorizedAt = stateStartedAtMs;
      testTimeMs += 10;
      command("START_TREE:" + std::to_string(token));
      assert(stateStartedAtMs == authorizedAt);
      updateTree(authorizedAt + stagedDelayMs - 1);
      assert(treeState == TreeState::StagingHold);
      updateTree(authorizedAt + stagedDelayMs);
      assert(treeState == TreeState::RaceActive);

      // Arm is atomic, even when replacing an already active automatic race.
      starterButtonMode = false;
      command("ARM:MANUAL");
      assert(starterButtonMode && treeState == TreeState::WaitingForAllLanes && !startAuthorized);
      updateTree(testTimeMs);
      command("START_TREE:" + std::to_string(stagingToken));
      assert(startAuthorized);
      stage(false);
      updateTree(testTimeMs);
      assert(treeState == TreeState::WaitingForAllLanes && !startAuthorized);
      stage(true);
      updateTree(testTimeMs);
      const auto restagedToken = stagingToken;
      assert(restagedToken != token);
      command("START_TREE:" + std::to_string(token));
      assert(!startAuthorized);
      lightTestEnabled = true;
      command("START_TREE:" + std::to_string(restagedToken));
      assert(!startAuthorized);
      lightTestEnabled = false;
      stagedDelayMs = 0;
      command("START_TREE:" + std::to_string(restagedToken));
      updateTree(testTimeMs);
      assert(treeState == TreeState::RaceActive);

      command("ARM:AUTO");
      assert(!starterButtonMode);
      updateTree(testTimeMs);
      updateTree(testTimeMs);
      assert(treeState == TreeState::RaceActive);
      command("START_TREE:" + std::to_string(stagingToken));
      assert(!startAuthorized);
    }
  }
  std::cout << "Firmware starter-button tests passed.\n";
}

int main() {
  starterTests();
  for (RaceMode mode : {RaceMode::HeadsUp, RaceMode::Bracket}) {
    resetTest(mode);
    for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
      lanes[lane].launched = lanes[lane].finished = true;
      lanes[lane].launchedAtUs = 1000;
      lanes[lane].finishedAtUs = 10001000UL + lane;
      prepareElapsedResult(lane);
    }
    // Begin with no room at all, then drain one byte per polling iteration.
    for (uint8_t slot = 0; slot < SERIAL_QUEUE_CAPACITY; ++slot)
      assert(queueProtocolMessage("ACK:PING"));
    prepareRaceResults();
    enterState(TreeState::ShowingResults, 0);
    serviceRaceResults();
    assert(resultFieldCursor[0] == 0);
    testTimeMs = RESULT_HOLD_MS + 100;
    updateTree(testTimeMs);
    assert(treeState == TreeState::ShowingResults);
    drain();
    assert(droppedSerialMessageCount == 0);
    assert(count(Serial.output, ":ELAPSED_US:") == 4);
    assert(count(Serial.output, ":INTERVAL_1_UNAVAILABLE:") == 4);
    assert(count(Serial.output, ":INTERVAL_2_UNAVAILABLE:") == 4);
    assert(count(Serial.output, "RESULT:PLACE:") == 4);
    assert(count(Serial.output, "EVENT:TREE:RACE_COMPLETE:") == 1);
    assert(Serial.output.rfind("EVENT:TREE:RACE_COMPLETE:") > Serial.output.rfind("RESULT:"));
    assert(count(Serial.output, "RESULT:WINNER:") == (mode == RaceMode::Bracket ? 1 : 0));
  }

  resetTest(RaceMode::Bracket);
  treeState = TreeState::RaceActive;
  lanes[0].launched = lanes[0].finished = true;
  lanes[0].finishedAtUs = 10000000UL;
  prepareElapsedResult(0);
  drain();
  assert(count(Serial.output, ":ELAPSED_US:") == 1);
  assert(count(Serial.output, "RACE_COMPLETE") == 0);

  for (bool foul : {false, true}) {
    resetTest(RaceMode::Bracket);
    for (auto& lane : lanes) {
      lane.fouled = foul;
      lane.reactionAvailable = foul;
      lane.reactionUs = -1000;
    }
    prepareRaceResults();
    enterState(TreeState::ShowingResults, 0);
    drain();
    assert(count(Serial.output, "EVENT:TREE:RACE_COMPLETE:") == 1);
    assert(count(Serial.output, ":DNF:") == (foul ? 0 : 4));
    assert(count(Serial.output, ":FOUL:") == (foul ? 4 : 0));
    assert(count(Serial.output, "RESULT:NO_WINNER:") == (foul ? 0 : 1));
  }

  resetTest(RaceMode::Bracket);
  prepareRaceResults();
  enterState(TreeState::WaitingForAllLanes, 0);
  assert(!raceResultsPending);
  std::cout << "Firmware result delivery tests passed.\n";
}

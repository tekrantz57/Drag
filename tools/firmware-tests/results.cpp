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

int main() {
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

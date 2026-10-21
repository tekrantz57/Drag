#define DRAG_TEST_DUE
#define main sharedTestsMain
#include "results.cpp"
#undef main

void timeUs(uint32_t value) {
  testTimeMs = value / 1000;
  testSubmillisUs = value % 1000;
}

void edge(uint8_t lane, uint8_t sensor, bool blocked, uint32_t atUs) {
  timeUs(atUs);
  const auto pin = SENSOR_PINS[lane][sensor];
  assert(testInterruptMask == 0 && testInterruptCallbacks[pin] != nullptr);
  testPinLevels[pin] = blocked ? HIGH : LOW;
  testInterruptCallbacks[pin]();
}

int main() {
  assert(DragBoard::validPinMap());
  assert(SENSOR_PINS[3][PreStageSensor] == 50 && SENSOR_PINS[3][FinishSensor] == 53);
  for (auto mode : {RaceMode::HeadsUp, RaceMode::Bracket}) {
    resetTest(mode);
    stage(false);
    treeState = TreeState::RaceActive;
    splitLaneMask = 0x0F;
    // All four lanes: both edges of every downstream pulse occur without a scan.
    for (uint8_t lane = 0; lane < 4; ++lane) {
      lanes[lane].launched = true;
      lanes[lane].launchedAtUs = 1000;
      for (uint8_t sensor = Split1Sensor; sensor <= FinishSensor; ++sensor) {
        const uint32_t at = 10000U * sensor + lane * 100U;
        edge(lane, sensor, true, at);
        edge(lane, sensor, false, at + 40U);
      }
    }
    timeUs(80000);
    scanSensors(testTimeMs);
    for (uint8_t lane = 0; lane < 4; ++lane) {
      updateLaneRace(lane);
      assert(lanes[lane].crossedSplit1 && lanes[lane].crossedSplit2 && lanes[lane].crossedSpeedTrap);
      assert(lanes[lane].finished && lanes[lane].elapsedUs == 49000U + lane * 100U);
      assert(sensors[lane][FinishSensor].lastRawPulseWidthUs() == 40);
      assert(!sensors[lane][FinishSensor].isRawBlocked());
    }
  }

  resetTest(RaceMode::HeadsUp);
  stage(false);
  for (uint32_t pulse = 0; pulse < 5; ++pulse) {
    edge(0, FinishSensor, true, 10000U + pulse * 100U);
    edge(0, FinishSensor, false, 10040U + pulse * 100U);
  }
  timeUs(20000);
  scanSensors(testTimeMs);
  assert(sensors[0][FinishSensor].blockedEdgeCount() == 5);
  assert(sensors[0][FinishSensor].blockedAtUs() == 10000);
  assert(sensors[0][FinishSensor].becameBlocked());
  scanSensors(testTimeMs);
  assert(!sensors[0][FinishSensor].becameBlocked());

  resetTest(RaceMode::HeadsUp);
  stage(true);
  treeMode = TreeMode::Full;
  treeState = TreeState::RaceActive;
  raceEpochUs = 0;
  edge(0, StageSensor, false, 1600123);
  timeUs(1601000);
  scanSensors(testTimeMs);
  assert(sensors[0][StageSensor].isBlocked());
  timeUs(1603000);
  scanSensors(testTimeMs);
  updateLaneRace(0);
  assert(lanes[0].launched && lanes[0].launchedAtUs == 1600123);
  assert(lanes[0].reactionUs == 100123);

  // A sub-2 ms stage pulse is visible diagnostically but not accepted as staging.
  stage(false);
  edge(0, StageSensor, true, 2000000);
  edge(0, StageSensor, false, 2000400);
  timeUs(2010000);
  scanSensors(testTimeMs);
  assert(!sensors[0][StageSensor].isBlocked() && !sensors[0][StageSensor].becameBlocked());
  assert(sensors[0][StageSensor].blockedEdgeCount() == 1);

  edge(0, FinishSensor, true, UINT32_MAX - 19U);
  edge(0, FinishSensor, false, 20U);
  scanSensors(testTimeMs);
  assert(sensors[0][FinishSensor].lastRawPulseWidthUs() == 40U);
  resetSensorDiagnostics();
  scanSensors(testTimeMs);
  assert(sensors[0][FinishSensor].blockedEdgeCount() == 0);
  assert(!sensors[0][FinishSensor].hasCompletedRawPulse());

  testInterruptMask = 1;
  sensors[0][FinishSensor].resetDiagnostics();
  assert(testInterruptMask == 1);
  testInterruptMask = 0;
  command("IDENTIFY");
  assert(Serial.output.find("HELLO:DRAG_MC_DUE:0.1.2:PROTO:11:MCU:SAM3X8E") != std::string::npos);
  // Simulate all 24 disconnected inputs toggling without any main-loop time.
  stage(false);
  for (uint8_t lane = 0; lane < 4; ++lane) {
    for (uint8_t sensor = 0; sensor < 6; ++sensor) {
      for (uint8_t change = 0; change < 32; ++change)
        edge(lane, sensor, (change % 2) == 0, 1000U + change);
      assert(sensors[lane][sensor].captureFault());
      assert(testInterruptCallbacks[SENSOR_PINS[lane][sensor]] == nullptr);
    }
  }
  assert(!allLanesAreStaged());
  scanSensors(testTimeMs);
  command("PING");
  command("STATUS");
  assert(Serial.output.find("ACK:PING") != std::string::npos);
  assert(Serial.output.find("ERROR:SENSOR_CAPTURE_NOISE") != std::string::npos);
  assert(!sensors[0][FinishSensor].becameBlocked());
  resetSensorDiagnostics();
  assert(!sensors[0][FinishSensor].captureFault());
  assert(testInterruptCallbacks[SENSOR_PINS[0][FinishSensor]] != nullptr);
  // Idle is safe with no sensors. Monitoring has an independent, leased scope.
  resetTest(RaceMode::HeadsUp);
  stage(false);
  dueRaceCaptureArmed = false;
  splitLaneMask = 0;
  refreshSensorCaptureScope();
  for (const auto& lane : SENSOR_PINS)
    for (auto pin : lane) assert(testInterruptCallbacks[pin] == nullptr);
  command("SENSOR_MONITOR:START:1");
  assert(sensors[0][PreStageSensor].isEnabled() && sensors[0][FinishSensor].isEnabled());
  assert(!sensors[0][Split1Sensor].isEnabled() && !sensors[1][PreStageSensor].isEnabled());
  command("SENSOR_MONITOR:START:4");
  assert(!sensors[0][FinishSensor].isEnabled() && sensors[3][FinishSensor].isEnabled());
  command("SENSOR_MONITOR:START:4,");
  assert(sensorMonitorLaneMask == 8);
  command("SENSOR_MONITOR:START:1,1");
  assert(sensorMonitorLaneMask == 8);
  command("SENSOR_MONITOR:START:NONE");
  assert(!sensors[3][FinishSensor].isEnabled());
  command("SENSOR_MONITOR:START:2");
  updateSensorMonitor(sensorMonitorRenewedAtMs + SENSOR_MONITOR_LEASE_MS);
  assert(!sensors[1][FinishSensor].isEnabled());

  command("SET:HEAT_LANES:1");
  command("SENSOR_MONITOR:START:4");
  command("ARM:MANUAL");
  assert(sensors[0][FinishSensor].isEnabled() && !sensors[3][FinishSensor].isEnabled());
  edge(0, FinishSensor, true, 90000);
  edge(0, FinishSensor, false, 90100);
  command("ARM:MANUAL");
  scanSensors(testTimeMs);
  assert(!sensors[0][FinishSensor].becameBlocked());
  assert(sensors[0][FinishSensor].blockedEdgeCount() == 1);
  command("SET:INTERVAL_LANES:1");
  refreshSensorCaptureScope();
  assert(sensors[0][Split1Sensor].isEnabled() && sensors[0][Split2Sensor].isEnabled());
  enterState(TreeState::ShowingResults, testTimeMs);
  refreshSensorCaptureScope();
  assert(!sensors[0][FinishSensor].isEnabled() && sensors[3][FinishSensor].isEnabled());
  command("SENSOR_MONITOR:STOP");
  assert(!sensors[3][FinishSensor].isEnabled());
  // A high level at enable is a baseline, not a crossing or completed pulse.
  testPinLevels[SENSOR_PINS[0][FinishSensor]] = HIGH;
  command("SENSOR_MONITOR:START:1");
  scanSensors(testTimeMs);
  assert(!sensors[0][FinishSensor].becameBlocked());
  assert(sensors[0][FinishSensor].blockedEdgeCount() == 1);
  std::cout << "Due ISR pulse, staging, clock-wrap, identity and pin-map tests passed.\n";
  return sharedTestsMain();
}

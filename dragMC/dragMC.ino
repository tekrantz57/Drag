#include <Arduino.h>
#include <limits.h>

namespace {

constexpr uint8_t MAX_LANE_COUNT = 4;
constexpr uint8_t LIGHTS_PER_LANE = 7;
constexpr uint8_t SENSORS_PER_LANE = 4;
constexpr char FIRMWARE_NAME[] = "DRAG_MC";
constexpr char FIRMWARE_VERSION[] = "0.3.0";
constexpr uint8_t PROTOCOL_VERSION = 2;

enum LightIndex : uint8_t {
  PreStageLight,
  StageLight,
  AmberLight1,
  AmberLight2,
  AmberLight3,
  GreenLight,
  RedLight
};

enum SensorIndex : uint8_t {
  PreStageSensor,
  StageSensor,
  SpeedTrapSensor,
  FinishSensor
};

constexpr const char* SENSOR_NAMES[SENSORS_PER_LANE] = {
    "PRESTAGE", "STAGE", "SPEED_TRAP", "FINISH"};

constexpr uint8_t LIGHT_PINS[MAX_LANE_COUNT][LIGHTS_PER_LANE] = {
    {22, 23, 24, 25, 26, 27, 28},
    {29, 30, 31, 32, 33, 34, 35},
    {36, 37, 38, 39, 40, 41, 42},
    {43, 44, 45, 46, 47, 48, 49}
};

constexpr uint8_t SENSOR_PINS[MAX_LANE_COUNT][SENSORS_PER_LANE] = {
    {A0, A1, A2, A3},
    {A4, A5, A6, A7},
    {A8, A9, A10, A11},
    {A12, A13, A14, A15}
};

constexpr unsigned long DEFAULT_TRACK_LENGTH_IN_X1000 = 660000;
constexpr unsigned long DEFAULT_SPEED_TRAP_LENGTH_IN_X1000 = 12000;
constexpr unsigned long MIN_TRACK_LENGTH_IN_X1000 = 1000;
constexpr unsigned long MAX_TRACK_LENGTH_IN_X1000 = 10000000;
constexpr unsigned long MIN_SPEED_TRAP_LENGTH_IN_X1000 = 100;
// LM393 slot sensors used on this track drive the digital output HIGH when the
// beam is blocked and LOW when it is clear.
constexpr bool SENSOR_IS_ACTIVE_LOW = false;
constexpr unsigned long SENSOR_DEBOUNCE_MS = 2;
constexpr unsigned long STAGING_HOLD_MS = 500;
constexpr unsigned long AMBER_INTERVAL_MS = 500;
constexpr unsigned long RESULT_HOLD_MS = 3000;
constexpr unsigned long TRACK_CLEAR_HOLD_MS = 1000;
constexpr unsigned long MAX_RACE_TIME_MS = 30000;
constexpr unsigned long MIN_DIAL_MS = 100;
constexpr unsigned long MAX_DIAL_MS = 60000;
constexpr unsigned long DEFAULT_DIAL_MS = 10000;
constexpr uint8_t SERIAL_QUEUE_CAPACITY = 24;
constexpr uint8_t SERIAL_FRAME_SIZE = 136;
constexpr uint8_t MAX_SERIAL_INPUT_BYTES_PER_LOOP = 32;
constexpr unsigned long HEARTBEAT_INTERVAL_MS = 1000;

enum class RaceMode : uint8_t { HeadsUp, Bracket };
enum class TreeState : uint8_t {
  WaitingForAllLanes,
  StagingHold,
  RaceActive,
  ShowingResults,
  WaitingForClear
};
enum class LaneTreeStep : uint8_t {
  Waiting,
  Amber1,
  Amber2,
  Amber3,
  Green
};

class DebouncedBeamSensor {
 public:
  void begin(const uint8_t pin) {
    pin_ = pin;
    pinMode(pin_, SENSOR_IS_ACTIVE_LOW ? INPUT_PULLUP : INPUT);
    rawBlocked_ = readRawBlocked();
    blocked_ = rawBlocked_;
    const unsigned long nowUs = micros();
    rawChangedAtMs_ = millis();
    rawChangedAtUs_ = nowUs;
    rawPulseStartedAtUs_ = nowUs;
  }

  void update(const unsigned long nowMs) {
    const unsigned long nowUs = micros();
    becameBlocked_ = false;
    becameUnblocked_ = false;

    const bool newRawBlocked = readRawBlocked();
    if (newRawBlocked != rawBlocked_) {
      if (newRawBlocked) {
        if (blockedEdgeCount_ < ULONG_MAX) ++blockedEdgeCount_;
        rawPulseStartedAtUs_ = nowUs;
      } else {
        lastRawPulseWidthUs_ = nowUs - rawPulseStartedAtUs_;
        hasCompletedRawPulse_ = true;
      }
      rawBlocked_ = newRawBlocked;
      rawChangedAtMs_ = nowMs;
      rawChangedAtUs_ = nowUs;
    }

    if (blocked_ == rawBlocked_ ||
        nowMs - rawChangedAtMs_ < SENSOR_DEBOUNCE_MS) {
      return;
    }

    blocked_ = rawBlocked_;
    if (blocked_) {
      blockedAtUs_ = rawChangedAtUs_;
      becameBlocked_ = true;
    } else {
      unblockedAtUs_ = rawChangedAtUs_;
      becameUnblocked_ = true;
    }
  }

  bool isBlocked() const { return blocked_; }
  bool becameBlocked() const { return becameBlocked_; }
  bool becameUnblocked() const { return becameUnblocked_; }
  unsigned long blockedAtUs() const { return blockedAtUs_; }
  unsigned long unblockedAtUs() const { return unblockedAtUs_; }
  bool isRawBlocked() const { return rawBlocked_; }
  unsigned long blockedEdgeCount() const { return blockedEdgeCount_; }
  bool hasCompletedRawPulse() const { return hasCompletedRawPulse_; }
  unsigned long lastRawPulseWidthUs() const { return lastRawPulseWidthUs_; }

  void resetDiagnostics() {
    blockedEdgeCount_ = 0;
    lastRawPulseWidthUs_ = 0;
    hasCompletedRawPulse_ = false;
    if (rawBlocked_) rawPulseStartedAtUs_ = micros();
  }

 private:
  bool readRawBlocked() const {
    const bool inputIsHigh = digitalRead(pin_) == HIGH;
    return SENSOR_IS_ACTIVE_LOW ? !inputIsHigh : inputIsHigh;
  }

  uint8_t pin_ = 0;
  bool rawBlocked_ = false;
  bool blocked_ = false;
  bool becameBlocked_ = false;
  bool becameUnblocked_ = false;
  unsigned long rawChangedAtMs_ = 0;
  unsigned long rawChangedAtUs_ = 0;
  unsigned long blockedAtUs_ = 0;
  unsigned long unblockedAtUs_ = 0;
  unsigned long rawPulseStartedAtUs_ = 0;
  unsigned long blockedEdgeCount_ = 0;
  unsigned long lastRawPulseWidthUs_ = 0;
  bool hasCompletedRawPulse_ = false;
};

struct LaneRace {
  bool fouled = false;
  bool launched = false;
  bool crossedSpeedTrap = false;
  bool finished = false;
  bool elapsedAvailable = false;
  LaneTreeStep treeStep = LaneTreeStep::Waiting;
  unsigned long greenAtUs = 0;
  unsigned long launchedAtUs = 0;
  unsigned long speedTrapAtUs = 0;
  unsigned long finishedAtUs = 0;
  unsigned long elapsedUs = 0;
};

DebouncedBeamSensor sensors[MAX_LANE_COUNT][SENSORS_PER_LANE];
LaneRace lanes[MAX_LANE_COUNT];
unsigned long dialMs[MAX_LANE_COUNT] = {
    DEFAULT_DIAL_MS, DEFAULT_DIAL_MS, DEFAULT_DIAL_MS, DEFAULT_DIAL_MS};
unsigned long trackLengthInX1000 = DEFAULT_TRACK_LENGTH_IN_X1000;
unsigned long speedTrapLengthInX1000 =
    DEFAULT_SPEED_TRAP_LENGTH_IN_X1000;

RaceMode raceMode = RaceMode::HeadsUp;
uint8_t activeLaneCount = MAX_LANE_COUNT;
uint8_t heatLaneMask = 0x0F;
TreeState treeState = TreeState::WaitingForAllLanes;
unsigned long stateStartedAtMs = 0;
unsigned long raceEpochMs = 0;
unsigned long raceEpochUs = 0;
unsigned long slowestDialMs = DEFAULT_DIAL_MS;
char serialOutputQueue[SERIAL_QUEUE_CAPACITY][SERIAL_FRAME_SIZE];
uint8_t serialQueueHead = 0;
uint8_t serialQueueTail = 0;
uint8_t serialQueueCount = 0;
uint8_t serialHeadOffset = 0;
unsigned int droppedSerialMessageCount = 0;
uint32_t protocolSequence = 0;
unsigned long lastHeartbeatAtMs = 0;

void formatHeatLanes(char* destination, const size_t destinationSize);
const char* treeStateName();

uint8_t calculateChecksum(const char* payload) {
  uint8_t checksum = 0;
  while (*payload != '\0') {
    checksum ^= static_cast<uint8_t>(*payload++);
  }
  return checksum;
}

bool messageGetsSequenceMetadata(const char* payload) {
  return strncmp(payload, "EVENT:", 6) == 0 ||
         strncmp(payload, "RESULT:", 7) == 0;
}

void sendProtocolMessage(const char* payload) {
  if (serialQueueCount >= SERIAL_QUEUE_CAPACITY) {
    if (droppedSerialMessageCount < UINT_MAX) {
      ++droppedSerialMessageCount;
    }
    return;
  }

  char sequencedPayload[SERIAL_FRAME_SIZE - 4];
  const char* payloadToSend = payload;
  if (messageGetsSequenceMetadata(payload)) {
    snprintf(
        sequencedPayload,
        sizeof(sequencedPayload),
        "%s:SEQ:%lu:MS:%lu",
        payload,
        static_cast<unsigned long>(++protocolSequence),
        millis());
    payloadToSend = sequencedPayload;
  }

  const uint8_t checksum = calculateChecksum(payloadToSend);
  snprintf(
      serialOutputQueue[serialQueueTail],
      SERIAL_FRAME_SIZE,
      "%s:%02X\n",
      payloadToSend,
      checksum);
  serialQueueTail = (serialQueueTail + 1) % SERIAL_QUEUE_CAPACITY;
  ++serialQueueCount;
}

void sendHello() {
  char heatLanes[10];
  char message[112];
  formatHeatLanes(heatLanes, sizeof(heatLanes));
  snprintf(
      message,
      sizeof(message),
      "HELLO:%s:%s:PROTO:%u:MCU:MEGA2560:LANES:%u:HEAT_LANES:%s",
      FIRMWARE_NAME,
      FIRMWARE_VERSION,
      PROTOCOL_VERSION,
      activeLaneCount,
      heatLanes);
  sendProtocolMessage(message);
}

void sendHeartbeat(const unsigned long nowMs) {
  char message[80];
  snprintf(
      message,
      sizeof(message),
      "HEARTBEAT:%lu:SEQ:%lu:STATE:%s",
      nowMs,
      static_cast<unsigned long>(protocolSequence),
      treeStateName());
  sendProtocolMessage(message);
}

void updateHeartbeat(const unsigned long nowMs) {
  if (nowMs - lastHeartbeatAtMs < HEARTBEAT_INTERVAL_MS) return;
  lastHeartbeatAtMs = nowMs;
  sendHeartbeat(nowMs);
}

void queueDroppedMessageWarning() {
  if (droppedSerialMessageCount == 0 ||
      serialQueueCount >= SERIAL_QUEUE_CAPACITY) {
    return;
  }

  const unsigned int droppedCount = droppedSerialMessageCount;
  droppedSerialMessageCount = 0;
  char message[48];
  snprintf(
      message,
      sizeof(message),
      "ERROR:SERIAL_QUEUE_DROPPED:%u",
      droppedCount);
  sendProtocolMessage(message);
}

void serviceSerialOutput() {
  if (serialQueueCount == 0) {
    queueDroppedMessageWarning();
    return;
  }

  const int availableBytes = Serial.availableForWrite();
  if (availableBytes <= 0) return;

  const char* frame = serialOutputQueue[serialQueueHead];
  const size_t frameLength = strlen(frame);
  const size_t remainingBytes = frameLength - serialHeadOffset;
  const size_t bytesToWrite =
      min(remainingBytes, static_cast<size_t>(availableBytes));

  Serial.write(
      reinterpret_cast<const uint8_t*>(frame + serialHeadOffset),
      bytesToWrite);
  serialHeadOffset += bytesToWrite;

  if (serialHeadOffset < frameLength) return;

  serialHeadOffset = 0;
  serialQueueHead = (serialQueueHead + 1) % SERIAL_QUEUE_CAPACITY;
  --serialQueueCount;
  queueDroppedMessageWarning();
}

void sendLaneEvent(const uint8_t lane, const char* event) {
  char message[64];
  snprintf(message, sizeof(message), "EVENT:LANE:%u:%s", lane + 1, event);
  sendProtocolMessage(message);
}

void setLaneLight(const uint8_t lane, const LightIndex light, const bool on) {
  digitalWrite(LIGHT_PINS[lane][light], on ? HIGH : LOW);
}

void turnOffLaneTree(const uint8_t lane) {
  setLaneLight(lane, AmberLight1, false);
  setLaneLight(lane, AmberLight2, false);
  setLaneLight(lane, AmberLight3, false);
  setLaneLight(lane, GreenLight, false);
}

void turnOffRaceLights() {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    turnOffLaneTree(lane);
    setLaneLight(lane, RedLight, false);
  }
}

void resetLaneResults() {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    lanes[lane] = LaneRace{};
  }
}

const char* raceModeName() {
  return raceMode == RaceMode::HeadsUp ? "HEADS_UP" : "BRACKET";
}

bool laneIsActive(const uint8_t lane) {
  return activeLaneCount == 4 || lane == 0 || lane == 3;
}

bool laneParticipates(const uint8_t lane) {
  return laneIsActive(lane) && (heatLaneMask & (1U << lane)) != 0;
}

uint8_t defaultHeatLaneMask() {
  return activeLaneCount == 4 ? 0x0F : 0x09;
}

void formatHeatLanes(char* destination, const size_t destinationSize) {
  destination[0] = '\0';
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    if (!laneParticipates(lane)) continue;
    char laneText[4];
    snprintf(laneText, sizeof(laneText), "%s%u",
             destination[0] == '\0' ? "" : ",", lane + 1);
    strncat(destination, laneText, destinationSize - strlen(destination) - 1);
  }
}

const char* treeStateName() {
  switch (treeState) {
    case TreeState::WaitingForAllLanes:
      return "WAITING_FOR_ALL_LANES";
    case TreeState::StagingHold:
      return "ALL_LANES_STAGED";
    case TreeState::RaceActive:
      return "RACE_ACTIVE";
    case TreeState::ShowingResults:
      return "RACE_COMPLETE";
    case TreeState::WaitingForClear:
      return "WAITING_FOR_CLEAR";
  }
  return "UNKNOWN";
}

void calculateSlowestDial() {
  slowestDialMs = 0;
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    if (!laneParticipates(lane)) continue;
    if (dialMs[lane] > slowestDialMs) slowestDialMs = dialMs[lane];
  }
}

unsigned long laneDelayMs(const uint8_t lane) {
  return raceMode == RaceMode::Bracket ? slowestDialMs - dialMs[lane] : 0;
}

unsigned long latestLaneDelayMs() {
  unsigned long latestDelay = 0;
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    if (!laneParticipates(lane)) continue;
    const unsigned long delay = laneDelayMs(lane);
    if (delay > latestDelay) latestDelay = delay;
  }
  return latestDelay;
}

void enterState(const TreeState newState, const unsigned long nowMs) {
  treeState = newState;
  stateStartedAtMs = nowMs;

  switch (newState) {
    case TreeState::WaitingForAllLanes:
      turnOffRaceLights();
      resetLaneResults();
      sendProtocolMessage("EVENT:TREE:WAITING_FOR_ALL_LANES");
      break;

    case TreeState::StagingHold:
      sendProtocolMessage("EVENT:TREE:ALL_LANES_STAGED");
      break;

    case TreeState::RaceActive:
      calculateSlowestDial();
      raceEpochMs = nowMs;
      raceEpochUs = micros();
      sendProtocolMessage(
          raceMode == RaceMode::Bracket
              ? "EVENT:TREE:BRACKET_START"
              : "EVENT:TREE:HEADS_UP_START");
      break;

    case TreeState::ShowingResults:
      sendProtocolMessage("EVENT:TREE:RACE_COMPLETE");
      break;

    case TreeState::WaitingForClear:
      turnOffRaceLights();
      sendProtocolMessage("EVENT:TREE:WAITING_FOR_CLEAR");
      break;
  }
}

bool allLanesAreStaged() {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    if (!laneParticipates(lane)) continue;
    if (!sensors[lane][PreStageSensor].isBlocked() ||
        !sensors[lane][StageSensor].isBlocked()) {
      return false;
    }
  }
  return true;
}

bool allLanesHaveResults() {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    if (!laneParticipates(lane)) continue;
    if (!lanes[lane].fouled && !lanes[lane].finished) return false;
  }
  return true;
}

bool allSensorsAreClear() {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    if (!laneParticipates(lane)) continue;
    for (uint8_t sensor = 0; sensor < SENSORS_PER_LANE; ++sensor) {
      if (sensors[lane][sensor].isBlocked()) return false;
    }
  }
  return true;
}

void updateStagingLights() {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    const bool active = laneParticipates(lane);
    setLaneLight(
        lane,
        PreStageLight,
        active && sensors[lane][PreStageSensor].isBlocked());
    setLaneLight(
        lane,
        StageLight,
        active && sensors[lane][StageSensor].isBlocked());
  }
}

void foulLane(const uint8_t lane) {
  if (lanes[lane].fouled) return;
  lanes[lane].fouled = true;
  turnOffLaneTree(lane);
  setLaneLight(lane, RedLight, true);
  sendLaneEvent(lane, "FOUL");
}

void setLaneTreeStep(const uint8_t lane, const LaneTreeStep step) {
  LaneRace& result = lanes[lane];
  if (result.fouled || result.treeStep == step) return;

  turnOffLaneTree(lane);
  result.treeStep = step;
  char event[24];

  switch (step) {
    case LaneTreeStep::Waiting:
      return;
    case LaneTreeStep::Amber1:
      setLaneLight(lane, AmberLight1, true);
      strcpy(event, "AMBER_1");
      break;
    case LaneTreeStep::Amber2:
      setLaneLight(lane, AmberLight2, true);
      strcpy(event, "AMBER_2");
      break;
    case LaneTreeStep::Amber3:
      setLaneLight(lane, AmberLight3, true);
      strcpy(event, "AMBER_3");
      break;
    case LaneTreeStep::Green:
      setLaneLight(lane, GreenLight, true);
      result.greenAtUs =
          raceEpochUs +
          (laneDelayMs(lane) + 3UL * AMBER_INTERVAL_MS) * 1000UL;
      strcpy(event, "GREEN");
      break;
  }
  sendLaneEvent(lane, event);
}

void updateLaneTree(const uint8_t lane, const unsigned long nowMs) {
  if (lanes[lane].fouled) return;

  const unsigned long elapsedMs = nowMs - raceEpochMs;
  const unsigned long startMs = laneDelayMs(lane);

  if (elapsedMs < startMs) return;
  if (elapsedMs < startMs + AMBER_INTERVAL_MS) {
    setLaneTreeStep(lane, LaneTreeStep::Amber1);
  } else if (elapsedMs < startMs + 2UL * AMBER_INTERVAL_MS) {
    setLaneTreeStep(lane, LaneTreeStep::Amber2);
  } else if (elapsedMs < startMs + 3UL * AMBER_INTERVAL_MS) {
    setLaneTreeStep(lane, LaneTreeStep::Amber3);
  } else {
    setLaneTreeStep(lane, LaneTreeStep::Green);
  }
}

void reportElapsedAndBreakout(const uint8_t lane) {
  LaneRace& result = lanes[lane];
  char message[64];

  if (!result.launched) {
    snprintf(
        message, sizeof(message),
        "RESULT:LANE:%u:ELAPSED_UNAVAILABLE", lane + 1);
    sendProtocolMessage(message);
    return;
  }

  result.elapsedUs = result.finishedAtUs - result.launchedAtUs;
  result.elapsedAvailable = true;
  snprintf(
      message, sizeof(message), "RESULT:LANE:%u:ELAPSED_US:%lu",
      lane + 1, result.elapsedUs);
  sendProtocolMessage(message);

  if (raceMode == RaceMode::Bracket &&
      result.elapsedUs < dialMs[lane] * 1000UL) {
    snprintf(
        message, sizeof(message), "RESULT:LANE:%u:BREAKOUT_US:%lu",
        lane + 1, dialMs[lane] * 1000UL - result.elapsedUs);
  } else {
    snprintf(message, sizeof(message), "RESULT:LANE:%u:VALID", lane + 1);
  }
  sendProtocolMessage(message);
}

void reportTrapSpeed(const uint8_t lane) {
  LaneRace& result = lanes[lane];
  char message[64];

  if (!result.crossedSpeedTrap) {
    snprintf(
        message, sizeof(message), "RESULT:LANE:%u:SPEED_UNAVAILABLE",
        lane + 1);
    sendProtocolMessage(message);
    return;
  }

  const unsigned long trapUs = result.finishedAtUs - result.speedTrapAtUs;
  if (trapUs == 0 || trapUs > MAX_RACE_TIME_MS * 1000UL) {
    snprintf(
        message, sizeof(message), "RESULT:LANE:%u:SPEED_INVALID", lane + 1);
    sendProtocolMessage(message);
    return;
  }

  const float speedTrapInches = speedTrapLengthInX1000 / 1000.0F;
  const float mph =
      (speedTrapInches / (trapUs / 1000000.0F)) / 17.6F;
  snprintf(
      message, sizeof(message), "RESULT:LANE:%u:SPEED_MPH_X100:%lu",
      lane + 1, static_cast<unsigned long>(mph * 100.0F + 0.5F));
  sendProtocolMessage(message);
}

void updateLaneRace(const uint8_t lane) {
  LaneRace& result = lanes[lane];
  if (result.fouled || result.finished) return;

  DebouncedBeamSensor& stage = sensors[lane][StageSensor];
  if (!result.launched && stage.becameUnblocked()) {
    const unsigned long launchUs = stage.unblockedAtUs();
    const unsigned long scheduledGreenUs =
        raceEpochUs +
        (laneDelayMs(lane) + 3UL * AMBER_INTERVAL_MS) * 1000UL;
    const int32_t reactionUs =
        static_cast<int32_t>(launchUs - scheduledGreenUs);

    if (reactionUs < 0) {
      foulLane(lane);
      return;
    }

    result.greenAtUs = scheduledGreenUs;
    result.launchedAtUs = launchUs;
    result.launched = true;
    char message[48];
    snprintf(
        message, sizeof(message), "EVENT:LANE:%u:REACTION_US:%ld",
        lane + 1, static_cast<long>(reactionUs));
    sendProtocolMessage(message);
  }

  DebouncedBeamSensor& trap = sensors[lane][SpeedTrapSensor];
  if (!result.crossedSpeedTrap && trap.becameBlocked()) {
    result.speedTrapAtUs = trap.blockedAtUs();
    result.crossedSpeedTrap = true;
    sendLaneEvent(lane, "SPEED_TRAP");
  }

  DebouncedBeamSensor& finish = sensors[lane][FinishSensor];
  if (finish.becameBlocked()) {
    result.finishedAtUs = finish.blockedAtUs();
    result.finished = true;
    reportElapsedAndBreakout(lane);
    reportTrapSpeed(lane);
  }
}

void reportWinner() {
  char message[40];

  if (raceMode == RaceMode::HeadsUp) {
    bool used[MAX_LANE_COUNT] = {};
    uint8_t place = 1;
    for (; place <= activeLaneCount; ++place) {
      int8_t bestLane = -1;
      unsigned long bestFinishOffset = 0;
      for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
        if (!laneParticipates(lane)) continue;
        if (used[lane] || lanes[lane].fouled || !lanes[lane].finished) continue;
        const unsigned long offset = lanes[lane].finishedAtUs - raceEpochUs;
        if (bestLane < 0 || offset < bestFinishOffset) {
          bestLane = lane;
          bestFinishOffset = offset;
        }
      }
      if (bestLane < 0) break;
      used[bestLane] = true;
      snprintf(
          message, sizeof(message), "RESULT:PLACE:%u:LANE:%u",
          place, bestLane + 1);
      sendProtocolMessage(message);
    }
    if (place == 1) sendProtocolMessage("RESULT:NO_WINNER");
    return;
  }

  int8_t winner = -1;
  unsigned long bestValue = 0;
  bool legalFinisherExists = false;

  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    if (!laneParticipates(lane)) continue;
    if (lanes[lane].fouled || !lanes[lane].finished ||
        !lanes[lane].elapsedAvailable) continue;
    const bool breakout = lanes[lane].elapsedUs < dialMs[lane] * 1000UL;
    if (breakout) continue;
    const unsigned long finishOffset = lanes[lane].finishedAtUs - raceEpochUs;
    if (!legalFinisherExists || finishOffset < bestValue) {
      winner = lane;
      bestValue = finishOffset;
      legalFinisherExists = true;
    }
  }

  if (!legalFinisherExists) {
    for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
      if (!laneParticipates(lane)) continue;
      if (lanes[lane].fouled || !lanes[lane].finished ||
          !lanes[lane].elapsedAvailable) continue;
      const unsigned long dialUs = dialMs[lane] * 1000UL;
      if (lanes[lane].elapsedUs >= dialUs) continue;
      const unsigned long breakoutUs = dialUs - lanes[lane].elapsedUs;
      if (winner < 0 || breakoutUs < bestValue) {
        winner = lane;
        bestValue = breakoutUs;
      }
    }
  }

  if (winner < 0) {
    sendProtocolMessage("RESULT:NO_WINNER");
  } else {
    snprintf(message, sizeof(message), "RESULT:WINNER:LANE:%u", winner + 1);
    sendProtocolMessage(message);
  }
}

void sendStatus() {
  char message[132];
  char heatLanes[10];
  formatHeatLanes(heatLanes, sizeof(heatLanes));
  snprintf(
      message,
      sizeof(message),
      "STATUS:TREE:%s:MODE:%s:LANES:%u:HEAT_LANES:%s:TRACK_IN_X1000:%lu:TRAP_IN_X1000:%lu",
      treeStateName(),
      raceModeName(),
      activeLaneCount,
      heatLanes,
      trackLengthInX1000,
      speedTrapLengthInX1000);
  sendProtocolMessage(message);

  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    snprintf(
        message, sizeof(message),
        "STATUS:LANE:%u:DIAL_MS:%lu:PRESTAGE:%u:STAGE:%u:SPEED_TRAP:%u:FINISH:%u:FOUL:%u:FINISHED:%u",
        lane + 1, dialMs[lane],
        sensors[lane][PreStageSensor].isBlocked() ? 1 : 0,
        sensors[lane][StageSensor].isBlocked() ? 1 : 0,
        sensors[lane][SpeedTrapSensor].isBlocked() ? 1 : 0,
        sensors[lane][FinishSensor].isBlocked() ? 1 : 0,
        lanes[lane].fouled ? 1 : 0,
        lanes[lane].finished ? 1 : 0);
    sendProtocolMessage(message);
  }
}

void sendSensorDiagnostics() {
  char message[132];
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    for (uint8_t sensor = 0; sensor < SENSORS_PER_LANE; ++sensor) {
      const DebouncedBeamSensor& beam = sensors[lane][sensor];
      if (beam.hasCompletedRawPulse()) {
        snprintf(
            message, sizeof(message),
            "SENSOR:%u:%s:RAW:%u:EDGES:%lu:PULSE_US:%lu",
            lane + 1,
            SENSOR_NAMES[sensor],
            beam.isRawBlocked() ? 1 : 0,
            beam.blockedEdgeCount(),
            beam.lastRawPulseWidthUs());
      } else {
        snprintf(
            message, sizeof(message),
            "SENSOR:%u:%s:RAW:%u:EDGES:%lu:PULSE_US:NONE",
            lane + 1,
            SENSOR_NAMES[sensor],
            beam.isRawBlocked() ? 1 : 0,
            beam.blockedEdgeCount());
      }
      sendProtocolMessage(message);
    }
  }
}

void resetSensorDiagnostics() {
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    for (uint8_t sensor = 0; sensor < SENSORS_PER_LANE; ++sensor) {
      sensors[lane][sensor].resetDiagnostics();
    }
  }
}

int8_t hexValue(const char value) {
  if (value >= '0' && value <= '9') return value - '0';
  if (value >= 'A' && value <= 'F') return value - 'A' + 10;
  if (value >= 'a' && value <= 'f') return value - 'a' + 10;
  return -1;
}

bool settingsCanChange() {
  return treeState == TreeState::WaitingForAllLanes ||
         treeState == TreeState::ShowingResults ||
         treeState == TreeState::WaitingForClear;
}

void processSetCommand(char* line) {
  if (!settingsCanChange()) {
    sendProtocolMessage("ERROR:STATE:RACE_ACTIVE");
    return;
  }

  char* savePointer = nullptr;
  char* setPart = strtok_r(line, ":", &savePointer);
  char* setting = strtok_r(nullptr, ":", &savePointer);
  char* value1 = strtok_r(nullptr, ":", &savePointer);
  char* value2 = strtok_r(nullptr, ":", &savePointer);
  char* extra = strtok_r(nullptr, ":", &savePointer);

  if (setPart == nullptr || strcmp(setPart, "SET") != 0 ||
      setting == nullptr || value1 == nullptr || extra != nullptr) {
    sendProtocolMessage("ERROR:COMMAND:SET");
    return;
  }

  if (strcmp(setting, "MODE") == 0 && value2 == nullptr) {
    if (strcmp(value1, "HEADS_UP") == 0) {
      raceMode = RaceMode::HeadsUp;
    } else if (strcmp(value1, "BRACKET") == 0) {
      raceMode = RaceMode::Bracket;
    } else {
      sendProtocolMessage("ERROR:VALUE:MODE");
      return;
    }
    char message[32];
    snprintf(message, sizeof(message), "ACK:SET:MODE:%s", raceModeName());
    sendProtocolMessage(message);
    return;
  }

  if (strcmp(setting, "LANES") == 0 && value2 == nullptr) {
    const unsigned long requestedCount = strtoul(value1, nullptr, 10);
    if (requestedCount != 2 && requestedCount != 4) {
      sendProtocolMessage("ERROR:VALUE:LANES");
      return;
    }
    activeLaneCount = requestedCount;
    heatLaneMask = defaultHeatLaneMask();
    turnOffRaceLights();
    updateStagingLights();
    char message[24];
    snprintf(
        message, sizeof(message), "ACK:SET:LANES:%u", activeLaneCount);
    sendProtocolMessage(message);
    return;
  }

  if (strcmp(setting, "HEAT_LANES") == 0 && value2 == nullptr) {
    uint8_t requestedMask = 0;
    char* laneSavePointer = nullptr;
    char* laneText = strtok_r(value1, ",", &laneSavePointer);
    while (laneText != nullptr) {
      const unsigned long laneNumber = strtoul(laneText, nullptr, 10);
      if (laneNumber < 1 || laneNumber > MAX_LANE_COUNT ||
          !laneIsActive(laneNumber - 1) ||
          (requestedMask & (1U << (laneNumber - 1))) != 0) {
        sendProtocolMessage("ERROR:VALUE:HEAT_LANES");
        return;
      }
      requestedMask |= 1U << (laneNumber - 1);
      laneText = strtok_r(nullptr, ",", &laneSavePointer);
    }
    if (requestedMask == 0) {
      sendProtocolMessage("ERROR:VALUE:HEAT_LANES");
      return;
    }
    heatLaneMask = requestedMask;
    turnOffRaceLights();
    updateStagingLights();
    char heatLanes[10];
    char message[36];
    formatHeatLanes(heatLanes, sizeof(heatLanes));
    snprintf(message, sizeof(message), "ACK:SET:HEAT_LANES:%s", heatLanes);
    sendProtocolMessage(message);
    return;
  }

  if (strcmp(setting, "DISTANCES") == 0 && value2 != nullptr) {
    const unsigned long requestedTrackLength = strtoul(value1, nullptr, 10);
    const unsigned long requestedTrapLength = strtoul(value2, nullptr, 10);
    if (requestedTrackLength < MIN_TRACK_LENGTH_IN_X1000 ||
        requestedTrackLength > MAX_TRACK_LENGTH_IN_X1000 ||
        requestedTrapLength < MIN_SPEED_TRAP_LENGTH_IN_X1000 ||
        requestedTrapLength >= requestedTrackLength) {
      sendProtocolMessage("ERROR:VALUE:DISTANCES");
      return;
    }

    trackLengthInX1000 = requestedTrackLength;
    speedTrapLengthInX1000 = requestedTrapLength;
    char message[72];
    snprintf(
        message,
        sizeof(message),
        "ACK:SET:DISTANCES:%lu:%lu",
        trackLengthInX1000,
        speedTrapLengthInX1000);
    sendProtocolMessage(message);
    return;
  }

  if (strcmp(setting, "DIAL") == 0 && value2 != nullptr) {
    const unsigned long laneNumber = strtoul(value1, nullptr, 10);
    const unsigned long valueMs = strtoul(value2, nullptr, 10);
    if (laneNumber < 1 || laneNumber > MAX_LANE_COUNT ||
        !laneIsActive(laneNumber - 1) ||
        valueMs < MIN_DIAL_MS || valueMs > MAX_DIAL_MS) {
      sendProtocolMessage("ERROR:VALUE:DIAL");
      return;
    }
    dialMs[laneNumber - 1] = valueMs;
    char message[48];
    snprintf(
        message, sizeof(message), "ACK:SET:DIAL:%lu:%lu",
        laneNumber, valueMs);
    sendProtocolMessage(message);
    return;
  }

  sendProtocolMessage("ERROR:COMMAND:SET");
}

void processCommand(char* line) {
  char* checksumSeparator = strrchr(line, ':');
  if (checksumSeparator == nullptr || strlen(checksumSeparator + 1) != 2) {
    sendProtocolMessage("ERROR:CHECKSUM");
    return;
  }

  const int8_t high = hexValue(checksumSeparator[1]);
  const int8_t low = hexValue(checksumSeparator[2]);
  if (high < 0 || low < 0) {
    sendProtocolMessage("ERROR:CHECKSUM");
    return;
  }

  const uint8_t received = static_cast<uint8_t>((high << 4) | low);
  *checksumSeparator = '\0';
  if (received != calculateChecksum(line)) {
    sendProtocolMessage("ERROR:CHECKSUM");
    return;
  }

  if (strcmp(line, "PING") == 0) {
    sendProtocolMessage("ACK:PING");
  } else if (strcmp(line, "STATUS") == 0) {
    sendStatus();
  } else if (strcmp(line, "SENSOR_DIAGNOSTICS") == 0) {
    sendSensorDiagnostics();
  } else if (strcmp(line, "RESET_SENSOR_DIAGNOSTICS") == 0) {
    resetSensorDiagnostics();
    sendProtocolMessage("ACK:RESET_SENSOR_DIAGNOSTICS");
  } else if (strcmp(line, "RESET") == 0) {
    sendProtocolMessage("ACK:RESET");
    enterState(TreeState::WaitingForAllLanes, millis());
  } else if (strncmp(line, "SET:", 4) == 0) {
    processSetCommand(line);
  } else {
    char message[112];
    snprintf(message, sizeof(message), "ERROR:COMMAND:%s", line);
    sendProtocolMessage(message);
  }
}

void updateSerialCommands() {
  static char buffer[96];
  static uint8_t length = 0;
  static bool overflowed = false;
  static bool invalidCharacters = false;

  uint8_t processedByteCount = 0;
  while (Serial.available() > 0 &&
         processedByteCount < MAX_SERIAL_INPUT_BYTES_PER_LOOP) {
    ++processedByteCount;
    const char value = static_cast<char>(Serial.read());
    if (value == '\r') continue;

    if (value == '\n') {
      if (invalidCharacters) {
        sendProtocolMessage("ERROR:COMMAND:INVALID_CHARACTERS");
      } else if (overflowed) {
        sendProtocolMessage("ERROR:COMMAND:TOO_LONG");
      } else if (length > 0) {
        buffer[length] = '\0';
        processCommand(buffer);
      }
      length = 0;
      overflowed = false;
      invalidCharacters = false;
      continue;
    }

    const uint8_t byteValue = static_cast<uint8_t>(value);
    if (byteValue < 0x20 || byteValue > 0x7E) {
      if (length > 0) invalidCharacters = true;
      continue;
    }
    if (overflowed || invalidCharacters) continue;
    if (length >= sizeof(buffer) - 1) {
      overflowed = true;
      continue;
    }
    buffer[length++] = value;
  }
}

void updateTree(const unsigned long nowMs) {
  if (treeState == TreeState::StagingHold) {
    for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
      if (!laneParticipates(lane)) continue;
      if (!sensors[lane][StageSensor].isBlocked()) foulLane(lane);
    }
  }

  switch (treeState) {
    case TreeState::WaitingForAllLanes:
      if (allLanesAreStaged()) enterState(TreeState::StagingHold, nowMs);
      break;

    case TreeState::StagingHold:
      if (nowMs - stateStartedAtMs >= STAGING_HOLD_MS) {
        enterState(TreeState::RaceActive, nowMs);
      }
      break;

    case TreeState::RaceActive: {
      for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
        if (!laneParticipates(lane)) continue;
        updateLaneTree(lane, nowMs);
        updateLaneRace(lane);
      }

      const unsigned long latestGreenDelay =
          latestLaneDelayMs() +
          3UL * AMBER_INTERVAL_MS;
      if (allLanesHaveResults()) {
        reportWinner();
        enterState(TreeState::ShowingResults, nowMs);
      } else if (nowMs - raceEpochMs >=
                 latestGreenDelay + MAX_RACE_TIME_MS) {
        for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
          if (!laneParticipates(lane)) continue;
          if (!lanes[lane].fouled && !lanes[lane].finished) {
            char message[28];
            snprintf(
                message, sizeof(message), "RESULT:LANE:%u:DNF", lane + 1);
            sendProtocolMessage(message);
          }
        }
        reportWinner();
        enterState(TreeState::ShowingResults, nowMs);
      }
      break;
    }

    case TreeState::ShowingResults:
      if (nowMs - stateStartedAtMs >= RESULT_HOLD_MS) {
        enterState(TreeState::WaitingForClear, nowMs);
      }
      break;

    case TreeState::WaitingForClear:
      if (!allSensorsAreClear()) {
        stateStartedAtMs = nowMs;
      } else if (nowMs - stateStartedAtMs >= TRACK_CLEAR_HOLD_MS) {
        enterState(TreeState::WaitingForAllLanes, nowMs);
      }
      break;
  }
}

}  // namespace

void setup() {
  Serial.begin(115200);
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    for (uint8_t light = 0; light < LIGHTS_PER_LANE; ++light) {
      pinMode(LIGHT_PINS[lane][light], OUTPUT);
      digitalWrite(LIGHT_PINS[lane][light], LOW);
    }
    for (uint8_t sensor = 0; sensor < SENSORS_PER_LANE; ++sensor) {
      sensors[lane][sensor].begin(SENSOR_PINS[lane][sensor]);
    }
  }
  const unsigned long nowMs = millis();
  sendHello();
  enterState(TreeState::WaitingForAllLanes, nowMs);
  updateStagingLights();
}

void loop() {
  const unsigned long nowMs = millis();
  for (uint8_t lane = 0; lane < MAX_LANE_COUNT; ++lane) {
    for (uint8_t sensor = 0; sensor < SENSORS_PER_LANE; ++sensor) {
      sensors[lane][sensor].update(nowMs);
    }
  }
  updateSerialCommands();
  updateStagingLights();
  updateTree(nowMs);
  updateHeartbeat(nowMs);
  serviceSerialOutput();
}

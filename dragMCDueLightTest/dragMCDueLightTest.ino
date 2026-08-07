#include <Arduino.h>

namespace {

constexpr uint8_t LANE_COUNT = 4;
constexpr uint8_t LIGHTS_PER_LANE = 7;
constexpr uint8_t PROTOCOL_VERSION = 8;
constexpr unsigned long HEARTBEAT_INTERVAL_MS = 1000;

enum LightIndex : uint8_t {
  PreStageLight,
  StageLight,
  AmberLight1,
  AmberLight2,
  AmberLight3,
  GreenLight,
  RedLight
};

constexpr uint8_t LIGHT_PINS[LANE_COUNT][LIGHTS_PER_LANE] = {
    {22, 23, 24, 25, 26, 27, 28},
    {29, 30, 31, 32, 33, 34, 35},
    {36, 37, 38, 39, 40, 41, 42},
    {43, 44, 45, 46, 47, 48, 49}
};

bool lightTestActive = false;
unsigned long lastHeartbeatAtMs = 0;

uint8_t calculateChecksum(const char* text) {
  uint8_t checksum = 0;
  while (*text != '\0') checksum ^= static_cast<uint8_t>(*text++);
  return checksum;
}

int8_t hexValue(const char value) {
  if (value >= '0' && value <= '9') return value - '0';
  if (value >= 'A' && value <= 'F') return value - 'A' + 10;
  if (value >= 'a' && value <= 'f') return value - 'a' + 10;
  return -1;
}

void sendProtocolMessage(const char* payload) {
  char frame[144];
  snprintf(
      frame,
      sizeof(frame),
      "%s:%02X\n",
      payload,
      calculateChecksum(payload));
  Serial.print(frame);
}

void sendHello() {
  sendProtocolMessage(
      "HELLO:DRAG_MC_DUE_LIGHT_TEST:0.1.0:PROTO:8:MCU:SAM3X8E:"
      "LANES:4:HEAT_LANES:1,2,3,4");
}

void sendHeartbeat(const unsigned long nowMs) {
  char message[72];
  snprintf(
      message,
      sizeof(message),
      "HEARTBEAT:%lu:SEQ:0:STATE:LIGHT_TEST_ONLY",
      nowMs);
  sendProtocolMessage(message);
}

void setLaneLight(
    const uint8_t lane,
    const LightIndex light,
    const bool on) {
  digitalWrite(LIGHT_PINS[lane][light], on ? HIGH : LOW);
}

void turnOffAllLights() {
  for (uint8_t lane = 0; lane < LANE_COUNT; ++lane) {
    for (uint8_t light = 0; light < LIGHTS_PER_LANE; ++light) {
      setLaneLight(lane, static_cast<LightIndex>(light), false);
    }
  }
}

bool tryParseLightName(const char* value, LightIndex& light) {
  if (strcmp(value, "PRESTAGE") == 0) {
    light = PreStageLight;
  } else if (strcmp(value, "STAGE") == 0) {
    light = StageLight;
  } else if (strcmp(value, "AMBER_1") == 0) {
    light = AmberLight1;
  } else if (strcmp(value, "AMBER_2") == 0) {
    light = AmberLight2;
  } else if (strcmp(value, "AMBER_3") == 0) {
    light = AmberLight3;
  } else if (strcmp(value, "GREEN") == 0) {
    light = GreenLight;
  } else if (strcmp(value, "RED") == 0) {
    light = RedLight;
  } else {
    return false;
  }
  return true;
}

void processLightTestCommand(char* line) {
  if (strcmp(line, "LIGHT_TEST:START") == 0) {
    if (!lightTestActive) {
      lightTestActive = true;
      turnOffAllLights();
    }
    sendProtocolMessage("ACK:LIGHT_TEST:START");
    return;
  }

  if (strcmp(line, "LIGHT_TEST:STOP") == 0) {
    lightTestActive = false;
    turnOffAllLights();
    sendProtocolMessage("ACK:LIGHT_TEST:STOP");
    return;
  }

  if (!lightTestActive) {
    sendProtocolMessage("ERROR:STATE:LIGHT_TEST_INACTIVE");
    return;
  }

  if (strcmp(line, "LIGHT_TEST:OFF") == 0) {
    turnOffAllLights();
    sendProtocolMessage("ACK:LIGHT_TEST:OFF");
    return;
  }

  char* savePointer = nullptr;
  char* command = strtok_r(line, ":", &savePointer);
  char* operation = strtok_r(nullptr, ":", &savePointer);
  char* laneText = strtok_r(nullptr, ":", &savePointer);
  char* lightText = strtok_r(nullptr, ":", &savePointer);
  char* stateText = strtok_r(nullptr, ":", &savePointer);
  char* extra = strtok_r(nullptr, ":", &savePointer);
  if (command == nullptr || operation == nullptr || laneText == nullptr ||
      lightText == nullptr || stateText == nullptr || extra != nullptr ||
      strcmp(command, "LIGHT_TEST") != 0 || strcmp(operation, "SET") != 0) {
    sendProtocolMessage("ERROR:COMMAND:LIGHT_TEST");
    return;
  }

  char* laneEnd = nullptr;
  const long laneNumber = strtol(laneText, &laneEnd, 10);
  if (laneEnd == laneText || *laneEnd != '\0' ||
      laneNumber < 1 || laneNumber > LANE_COUNT) {
    sendProtocolMessage("ERROR:VALUE:LIGHT_TEST_LANE");
    return;
  }

  LightIndex light = PreStageLight;
  if (!tryParseLightName(lightText, light)) {
    sendProtocolMessage("ERROR:VALUE:LIGHT_TEST_LIGHT");
    return;
  }
  if (strcmp(stateText, "0") != 0 && strcmp(stateText, "1") != 0) {
    sendProtocolMessage("ERROR:VALUE:LIGHT_TEST_STATE");
    return;
  }

  setLaneLight(
      static_cast<uint8_t>(laneNumber - 1),
      light,
      stateText[0] == '1');
  char message[64];
  snprintf(
      message,
      sizeof(message),
      "ACK:LIGHT_TEST:SET:%ld:%s:%s",
      laneNumber,
      lightText,
      stateText);
  sendProtocolMessage(message);
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
  } else if (strcmp(line, "IDENTIFY") == 0) {
    sendHello();
  } else if (strcmp(line, "STATUS") == 0) {
    sendProtocolMessage("STATUS:TREE:LIGHT_TEST_ONLY:MODE:DIAGNOSTIC");
  } else if (strcmp(line, "RESET") == 0) {
    lightTestActive = false;
    turnOffAllLights();
    sendProtocolMessage("ACK:RESET");
  } else if (strncmp(line, "LIGHT_TEST:", 11) == 0) {
    processLightTestCommand(line);
  } else {
    sendProtocolMessage("ERROR:MODE:LIGHT_TEST_ONLY");
  }
}

void updateSerialCommands() {
  static char buffer[96];
  static uint8_t length = 0;
  static bool overflowed = false;
  static bool invalidCharacters = false;

  while (Serial.available() > 0) {
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

    if (value < 0x20 || value > 0x7E) {
      invalidCharacters = true;
      continue;
    }
    if (length >= sizeof(buffer) - 1) {
      overflowed = true;
      continue;
    }
    buffer[length++] = value;
  }
}

}  // namespace

void setup() {
  for (uint8_t lane = 0; lane < LANE_COUNT; ++lane) {
    for (uint8_t light = 0; light < LIGHTS_PER_LANE; ++light) {
      pinMode(LIGHT_PINS[lane][light], OUTPUT);
      digitalWrite(LIGHT_PINS[lane][light], LOW);
    }
  }

  Serial.begin(115200);
  sendHello();
}

void loop() {
  const unsigned long nowMs = millis();
  updateSerialCommands();
  if (nowMs - lastHeartbeatAtMs >= HEARTBEAT_INTERVAL_MS) {
    lastHeartbeatAtMs = nowMs;
    sendHeartbeat(nowMs);
  }
}

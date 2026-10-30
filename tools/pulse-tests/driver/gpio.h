#pragma once
#include "Arduino.h"
using gpio_num_t = int;
using esp_err_t = int;
constexpr int ESP_OK = 0;
inline int gpio_hold_en(gpio_num_t) { return ESP_OK; }
inline int gpio_hold_dis(gpio_num_t) { return ESP_OK; }

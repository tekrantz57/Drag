#pragma once
#include "gpio.h"

union rmt_symbol_word_t {
  struct {
    uint32_t duration0 : 15, level0 : 1, duration1 : 15, level1 : 1;
  };
  uint32_t val;
};
constexpr int RMT_CLK_SRC_DEFAULT = 0;
struct rmt_tx_channel_config_t {
  int gpio_num, clk_src;
  uint32_t resolution_hz;
  size_t mem_block_symbols, trans_queue_depth;
  struct { bool init_level; } flags;
};
struct rmt_copy_encoder_config_t {};
struct rmt_transmit_config_t {
  struct { bool eot_level, queue_nonblocking; } flags;
};
struct MockRmt;
using rmt_channel_handle_t = MockRmt*;
struct rmt_tx_done_event_data_t {};
struct rmt_tx_event_callbacks_t {
  bool (*on_trans_done)(rmt_channel_handle_t, const rmt_tx_done_event_data_t*, void*);
};
struct MockRmt {
  int pin = 0;
  bool active = false, idle = false;
  uint32_t endAt = 0;
  rmt_tx_event_callbacks_t callbacks = {};
  void* context = nullptr;
};
using rmt_encoder_handle_t = MockRmt*;
inline MockRmt mockRmt;
inline bool mockRmtInitFails = false, mockRmtTransmitFails = false;
inline bool mockRmtCallbackFails = false, mockRmtImmediateComplete = false;
inline void mockRmtFinish() {
  mockRmt.active = false;
  testLevels[mockRmt.pin] = mockRmt.idle;
  if (mockRmt.callbacks.on_trans_done)
    mockRmt.callbacks.on_trans_done(&mockRmt, nullptr, mockRmt.context);
}
inline void mockRmtService() {
  if (mockRmt.active && static_cast<int32_t>(testClock - mockRmt.endAt) >= 0) mockRmtFinish();
}
inline int rmt_new_tx_channel(const rmt_tx_channel_config_t* c, rmt_channel_handle_t* out) {
  if (mockRmtInitFails) return -1;
  assert(c->resolution_hz == 1000000 && c->mem_block_symbols == 48);
  mockRmt = {};
  mockRmt.pin = c->gpio_num;
  mockRmt.idle = c->flags.init_level;
  testLevels[c->gpio_num] = mockRmt.idle;
  *out = &mockRmt;
  return ESP_OK;
}
inline int rmt_new_copy_encoder(const rmt_copy_encoder_config_t*, rmt_encoder_handle_t* out) {
  *out = &mockRmt;
  return ESP_OK;
}
inline int rmt_tx_register_event_callbacks(rmt_channel_handle_t c,
                                          const rmt_tx_event_callbacks_t* callbacks, void* context) {
  if (mockRmtCallbackFails) return -1;
  c->callbacks = *callbacks;
  c->context = context;
  return ESP_OK;
}
inline int rmt_enable(rmt_channel_handle_t) { testTimeHook = mockRmtService; return ESP_OK; }
inline int rmt_disable(rmt_channel_handle_t c) {
  c->active = false;
  testTimeHook = nullptr;
  return ESP_OK;
}
inline int rmt_del_channel(rmt_channel_handle_t) { return ESP_OK; }
inline int rmt_del_encoder(rmt_encoder_handle_t) { return ESP_OK; }
inline int rmt_transmit(rmt_channel_handle_t c, rmt_encoder_handle_t, const void* data,
                        size_t bytes, const rmt_transmit_config_t* cfg) {
  if (mockRmtTransmitFails) return -1;
  assert(bytes / sizeof(rmt_symbol_word_t) < 24);
  const auto* symbols = static_cast<const rmt_symbol_word_t*>(data);
  uint32_t duration = 0;
  for (size_t i = 0; i < bytes / sizeof(rmt_symbol_word_t); ++i) {
    assert(symbols[i].duration0 && symbols[i].duration1);
    duration += symbols[i].duration0 + symbols[i].duration1;
  }
  c->endAt = testClock + duration;
  c->active = true;
  c->idle = cfg->flags.eot_level;
  testLevels[c->pin] = symbols[0].level0;
  if (mockRmtImmediateComplete) mockRmtFinish();
  return ESP_OK;
}

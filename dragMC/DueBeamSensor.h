#pragma once

namespace DragDue {

// Preserve the caller's interrupt state; never enable interrupts inside an ISR.
class InterruptGuard {
 public:
  InterruptGuard() : previous_(__get_PRIMASK()) { __disable_irq(); }
  ~InterruptGuard() { __set_PRIMASK(previous_); }
 private:
  uint32_t previous_;
};

class BeamSensor {
 public:
  void begin(const uint8_t pin) {
    pin_ = pin;
    pinMode(pin_, INPUT);
    InterruptGuard guard;
    instances_[pin_] = this;
    detachInterrupt(pin_);
  }

  void setEnabled(const bool enabled) {
    if (enabled_ == enabled) return;
    InterruptGuard guard;
    detachInterrupt(pin_);
    enabled_ = enabled;
    pendingBlock_ = pendingUnblock_ = false;
    becameBlocked_ = becameUnblocked_ = false;
    unservicedInterrupts_ = 0;
    blocked_ = false;
    raw_ = rawBlocked_ = enabled && digitalRead(pin_) == HIGH;
    rawChangedUs_ = micros();
    pulseStartedValid_ = false;
    if (enabled && !noiseFault_) attachPinInterrupt(pin_);
  }
  bool isEnabled() const { return enabled_; }

  // ISR work is limited to state, timestamps and counters. No Serial or formatting.
  void captureEdge() {
    if (!enabled_ || noiseFault_) return;
    // Bound ISR work even if floating inputs prevent the main loop from running.
    // A disconnected/noisy pin is quarantined until an explicit diagnostic reset.
    if (++unservicedInterrupts_ >= 32) {
      noiseFault_ = true;
      detachInterrupt(pin_);
      return;
    }
    const uint32_t nowUs = micros();
    const bool blocked = digitalRead(pin_) == HIGH;
    if (blocked == rawBlocked_) return;
    rawBlocked_ = blocked;
    rawChangedUs_ = nowUs;
    if (blocked) {
      if (edgeCount_ < UINT32_MAX) ++edgeCount_;
      pulseStartedUs_ = nowUs;
      pulseStartedValid_ = true;
      if (!pendingBlock_) firstBlockUs_ = nowUs;
      pendingBlock_ = true;
    } else {
      if (pulseStartedValid_) {
        pulseWidthUs_ = nowUs - pulseStartedUs_;
        pulseAvailable_ = true;
        pulseStartedValid_ = false;
      }
      if (!pendingUnblock_) firstUnblockUs_ = nowUs;
      pendingUnblock_ = true;
    }
  }

  bool update(unsigned long, const bool immediate = false) {
    if (!enabled_) return false;
    Snapshot value;
    {
      InterruptGuard guard;
      value.raw = rawBlocked_;
      value.changedUs = rawChangedUs_;
      value.edges = edgeCount_;
      value.widthUs = pulseWidthUs_;
      value.pulseAvailable = pulseAvailable_;
      value.block = pendingBlock_;
      value.unblock = pendingUnblock_;
      value.blockUs = firstBlockUs_;
      value.unblockUs = firstUnblockUs_;
      pendingBlock_ = pendingUnblock_ = false;
      unservicedInterrupts_ = 0;
    }
    bool changed = value.block || value.unblock || edges_ != value.edges || raw_ != value.raw;
    raw_ = value.raw;
    edges_ = value.edges;
    widthUs_ = value.widthUs;
    hasPulse_ = value.pulseAvailable;
    becameBlocked_ = becameUnblocked_ = false;
    if (immediate) {
      // Both edges can have happened while the main loop was busy. Retain the
      // first crossing, even if the pin is clear again or has chattered since.
      becameBlocked_ = value.block;
      becameUnblocked_ = value.unblock;
      if (value.block) blockedAtUs_ = value.blockUs;
      if (value.unblock) unblockedAtUs_ = value.unblockUs;
      changed = changed || blocked_ != raw_;
      blocked_ = raw_;
    } else if (blocked_ != raw_ && static_cast<uint32_t>(micros() - value.changedUs) >= 2000U) {
      // Stage qualification is unchanged, but the transition timestamp came
      // from the interrupt rather than the later main-loop observation.
      blocked_ = raw_;
      if (blocked_) {
        blockedAtUs_ = value.changedUs;
        becameBlocked_ = true;
      } else {
        unblockedAtUs_ = value.changedUs;
        becameUnblocked_ = true;
      }
      changed = true;
    }
    return changed;
  }

  bool isBlocked() const { return !noiseFault_ && blocked_; }
  bool becameBlocked() const { return !noiseFault_ && becameBlocked_; }
  bool becameUnblocked() const { return !noiseFault_ && becameUnblocked_; }
  bool captureFault() const { return enabled_ && noiseFault_; }
  bool takeNoiseWarning() {
    if (!enabled_ || !noiseFault_ || noiseReported_) return false;
    noiseReported_ = true;
    return true;
  }
  unsigned long blockedAtUs() const { return blockedAtUs_; }
  unsigned long unblockedAtUs() const { return unblockedAtUs_; }
  bool isRawBlocked() const { return raw_; }
  unsigned long blockedEdgeCount() const { return edges_; }
  bool hasCompletedRawPulse() const { return hasPulse_; }
  unsigned long lastRawPulseWidthUs() const { return widthUs_; }

  void resetDiagnostics() {
    InterruptGuard guard;
    const bool reattach = noiseFault_;
    noiseFault_ = noiseReported_ = false;
    unservicedInterrupts_ = 0;
    if (reattach && enabled_) {
      pendingBlock_ = pendingUnblock_ = false;
      rawBlocked_ = digitalRead(pin_) == HIGH;
      rawChangedUs_ = micros();
      blocked_ = raw_ = false;  // Requalify staging after a noise fault.
      attachPinInterrupt(pin_);
    }
    edgeCount_ = edges_ = pulseWidthUs_ = widthUs_ = 0;
    pulseAvailable_ = hasPulse_ = false;
    pulseStartedValid_ = enabled_ && rawBlocked_;
    if (pulseStartedValid_) pulseStartedUs_ = micros();
  }

 private:
  struct Snapshot {
    bool raw, pulseAvailable, block, unblock;
    uint32_t changedUs, edges, widthUs, blockUs, unblockUs;
  };
  static BeamSensor* instances_[66];
  template<uint8_t Pin> static void interrupt() { instances_[Pin]->captureEdge(); }
  static void attachPinInterrupt(uint8_t pin) {
#define DRAG_ATTACH(Pin) case Pin: attachInterrupt(digitalPinToInterrupt(Pin), interrupt<Pin>, CHANGE); break
    switch (pin) {
      DRAG_ATTACH(A0); DRAG_ATTACH(A1); DRAG_ATTACH(A2); DRAG_ATTACH(A3);
      DRAG_ATTACH(A4); DRAG_ATTACH(A5); DRAG_ATTACH(A6); DRAG_ATTACH(A7);
      DRAG_ATTACH(A8); DRAG_ATTACH(A9); DRAG_ATTACH(A10); DRAG_ATTACH(A11);
      DRAG_ATTACH(2); DRAG_ATTACH(3); DRAG_ATTACH(4); DRAG_ATTACH(5);
      DRAG_ATTACH(6); DRAG_ATTACH(7); DRAG_ATTACH(8); DRAG_ATTACH(9);
      DRAG_ATTACH(50); DRAG_ATTACH(51); DRAG_ATTACH(52); DRAG_ATTACH(53);
    }
#undef DRAG_ATTACH
  }

  uint8_t pin_ = 0;
  volatile bool rawBlocked_ = false, pendingBlock_ = false, pendingUnblock_ = false, pulseAvailable_ = false;
  volatile uint32_t rawChangedUs_ = 0, pulseStartedUs_ = 0, firstBlockUs_ = 0, firstUnblockUs_ = 0;
  volatile uint32_t edgeCount_ = 0, pulseWidthUs_ = 0;
  volatile uint8_t unservicedInterrupts_ = 0;
  volatile bool noiseFault_ = false;
  volatile bool enabled_ = false, pulseStartedValid_ = false;
  bool noiseReported_ = false;
  bool raw_ = false, blocked_ = false, becameBlocked_ = false, becameUnblocked_ = false, hasPulse_ = false;
  uint32_t blockedAtUs_ = 0, unblockedAtUs_ = 0, edges_ = 0, widthUs_ = 0;
};

BeamSensor* BeamSensor::instances_[66] = {};

}  // namespace DragDue

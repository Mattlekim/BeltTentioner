#define POTENIOMETER_PIN A7
#define DEADZONE 40

int target = 512; // Default target
bool handshakeComplete = false;

void setup() {
  Serial.begin(9600);

  // Set digital pins 4, 5, 6 as outputs and drive them HIGH
  pinMode(4, OUTPUT);
  pinMode(5, OUTPUT);
  pinMode(6, OUTPUT);
  digitalWrite(4, HIGH);
  digitalWrite(5, HIGH);
  digitalWrite(6, HIGH);

  // Set PWM pins as outputs
  pinMode(3, OUTPUT); // Timer2
  pinMode(9, OUTPUT); // Timer1

  // Configure high-frequency PWM
  TCCR2B = TCCR2B & 0b11111000 | 0x01; // Pin 3
  TCCR1B = TCCR1B & 0b11111000 | 0x01; // Pin 9

  Serial.println("Waiting for handshake... Send 'HELLO' to begin.");
}

void loop() {
  // Always handle serial commands if present so target can be updated anytime
  while (Serial.available() > 0) {
    String input = Serial.readStringUntil('\n');
    input.trim();
    if (input.length() == 0) continue;

    if (input.equalsIgnoreCase("HELLO")) {
      if (!handshakeComplete) {
        handshakeComplete = true;
        Serial.println("READY");
        Serial.println("Analog\tTarget\tDistance\tPWM_Left\tPWM_Right\tDirection");
      }
    } else {
      int separatorIndex = input.indexOf(':');
      if (separatorIndex > 0) {
        String key = input.substring(0, separatorIndex);
        String valueStr = input.substring(separatorIndex + 1);
        int value = valueStr.toInt();

        if (key == "T" && value >= 0 && value <= 1023) {
          target = value;
        }
      }
    }
  }

  // If handshake hasn't completed yet, don't run control loop
  if (!handshakeComplete) {
    return;
  }

  int analogValue = analogRead(POTENIOMETER_PIN);
  int distance = abs(analogValue - target);

  static int lastPwmLeft = 0;
  static int lastPwmRight = 0;

  int pwmLeft = 0;
  int pwmRight = 0;
  String direction = "Neutral";

  if (distance > DEADZONE) {
    // Use distance minus deadzone so small movements are less aggressive
    int effectiveDistance = distance - DEADZONE;
    int maxDistance = 1023 - DEADZONE; // normalized range
    float x = (float)effectiveDistance / (float)maxDistance; // 0..1

    // Exponential mapping with increased steepness for more responsiveness
    const float beta = 4.0f; // larger beta => more aggressive
    float y = (exp(beta * x) - 1.0f) / (exp(beta) - 1.0f); // normalized 0..1

    // Increase maximum output to be closer to full power
    const int MAX_OUTPUT = 255 ; // near 255 but slightly reduced
    int pwmValue = (int)round(y * (float)MAX_OUTPUT);

    // Ensure within 0..MAX_OUTPUT
    pwmValue = constrain(pwmValue, 0, MAX_OUTPUT);

    // Raise minimum PWM to overcome stiction faster
    const int MIN_PWM = 40;
    if (pwmValue > 0 && pwmValue < MIN_PWM) pwmValue = MIN_PWM;

    // Ramp limiting: allow larger changes per loop for faster response
    const int MAX_DELTA_PER_LOOP = 60; // higher => faster

    if (analogValue < target) {
      // ramp left towards targetPwm
      int targetPwm = pwmValue;
      int delta = targetPwm - lastPwmLeft;
      if (delta > MAX_DELTA_PER_LOOP) delta = MAX_DELTA_PER_LOOP;
      if (delta < -MAX_DELTA_PER_LOOP) delta = -MAX_DELTA_PER_LOOP;
      pwmLeft = lastPwmLeft + delta;
      pwmLeft = constrain(pwmLeft, 0, MAX_OUTPUT);
      lastPwmLeft = pwmLeft;

      // ramp right down reasonably fast
      if (lastPwmRight > 0) {
        int down = MAX_DELTA_PER_LOOP; // match up-ramp speed
        lastPwmRight = max(0, lastPwmRight - down);
      }

      direction = "Anticlockwise";
    } else {
      // ramp right towards targetPwm
      int targetPwm = pwmValue;
      int delta = targetPwm - lastPwmRight;
      if (delta > MAX_DELTA_PER_LOOP) delta = MAX_DELTA_PER_LOOP;
      if (delta < -MAX_DELTA_PER_LOOP) delta = -MAX_DELTA_PER_LOOP;
      pwmRight = lastPwmRight + delta;
      pwmRight = constrain(pwmRight, 0, MAX_OUTPUT);
      lastPwmRight = pwmRight;

      // ramp left down reasonably fast
      if (lastPwmLeft > 0) {
        int down = MAX_DELTA_PER_LOOP; // match up-ramp speed
        lastPwmLeft = max(0, lastPwmLeft - down);
      }

      direction = "Clockwise";
    }
  } else {
    // inside deadzone -> ramp both PWMs down to zero smoothly
    const int MAX_DELTA_PER_LOOP = 40;
    if (lastPwmLeft > 0) {
      lastPwmLeft = max(0, lastPwmLeft - MAX_DELTA_PER_LOOP);
    }
    if (lastPwmRight > 0) {
      lastPwmRight = max(0, lastPwmRight - MAX_DELTA_PER_LOOP);
    }
    pwmLeft = lastPwmLeft;
    pwmRight = lastPwmRight;
    direction = "Neutral";
  }

  analogWrite(3, pwmLeft);
  analogWrite(9, pwmRight);

  // Output for Serial Plotter
  Serial.print(analogValue);
  Serial.print("\t");
  Serial.print(target);
  Serial.print("\t");
  Serial.print(distance);
  Serial.print("\t");
  Serial.print(pwmLeft);
  Serial.print("\t");
  Serial.print(pwmRight);
  Serial.print("\t");
  Serial.println(direction);

  // Shorten loop delay for faster response
  delay(40); // ms
}
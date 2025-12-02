#define POTENIOMETER_PIN A7

#define DEADZONE 12
#define DEADZONE_HYSTERESIS 6
#define MAX_OUTPUT 255
#define MIN_PWM 60
#define MAX_DELTA_PER_LOOP_ACTIVE 80
#define MAX_DELTA_PER_LOOP_IDLE 20

#define KILL_LOWER 10
#define KILL_UPPER 1014

#define RESTINGVALUE 44
#define MAX_TENTION_VALUE 200

// --- Timeout constants ---
#define TARGET_TIMEOUT_MS 5000    // 2 seconds for target reset
#define SESSION_TIMEOUT_MS 10000  // 10 seconds for handshake/session reset
#define DEFAULT_TARGET 20

#define UNWIDE_PIN 3
#define WIND_PIN 9

int target = DEFAULT_TARGET;
bool handshakeComplete = false;
bool stop_clockwise = false;
bool stop_antiClockwise = false;

unsigned long lastTargetSetTime = 0;  // Track when target was last updated
unsigned long lastActivityTime = 0;   // Track last activity (handshake or target set)

const bool FLIP_DIRECTION = true;

void setup() {
  Serial.begin(9600);


  pinMode(POTENIOMETER_PIN, INPUT);



  pinMode(UNWIDE_PIN, OUTPUT);  // Timer2
  pinMode(WIND_PIN, OUTPUT);    // Timer1

  TCCR2B = TCCR2B & 0b11111000 | 0x01;  // Pin 3
  TCCR1B = TCCR1B & 0b11111000 | 0x01;  // Pin 9

  Serial.println("Waiting for handshake... Send 'HELLO' to begin.");
}

int outputFrames = 0;
void loop() {

  //analogWrite(UNWIDE_PIN, 200);
  //analogWrite(WIND_PIN, 200);
  //return;
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
      lastActivityTime = millis();  // Update session activity
    } else if (input.equalsIgnoreCase("RESET")) {
      Serial.println("Kill flag reset to false.");
      lastActivityTime = millis();
    } else {
      int separatorIndex = input.indexOf(':');
      if (separatorIndex > 0) {
        String key = input.substring(0, separatorIndex);
        String valueStr = input.substring(separatorIndex + 1);
        int value = valueStr.toInt();
        if (key == "T" && value >= 0 && value <= 1023) {
          target = value;
          lastTargetSetTime = millis();  // Update target timestamp
          lastActivityTime = millis();   // Update session activity
        }
      }
    }
  }

  // --- Target timeout (only if handshake is complete) ---
  if (handshakeComplete && (millis() - lastTargetSetTime >= TARGET_TIMEOUT_MS)) {
    target = DEFAULT_TARGET;
    lastTargetSetTime = millis();
    Serial.print("Target timeout. Resetting to default: ");
    Serial.println(target);
  }

  // --- Session timeout (reset everything after 10s inactivity) ---
  if (handshakeComplete && (millis() - lastActivityTime >= SESSION_TIMEOUT_MS)) {
    handshakeComplete = false;
    target = DEFAULT_TARGET;
    analogWrite(UNWIDE_PIN, 0);
    analogWrite(WIND_PIN, 0);
    Serial.println("Session timeout. Handshake required again.");
    Serial.println("Waiting for handshake... Send 'HELLO' to begin.");
  }

  if (!handshakeComplete) return;

  int analogValue = analogRead(POTENIOMETER_PIN);

  stop_antiClockwise = false;
  stop_clockwise = false;
  if (analogValue <= KILL_LOWER) stop_antiClockwise = true;
  if (analogValue >= KILL_UPPER) stop_clockwise = true;

  if (stop_antiClockwise) {
    if (outputFrames >= 249) Serial.println("LOWER-LIMIT");
    analogWrite(UNWIDE_PIN, 0);
    analogWrite(WIND_PIN, 0);
  }

  if (stop_clockwise) {
    if (outputFrames >= 249) Serial.println("UPPER-LIMIT");
    analogWrite(UNWIDE_PIN, 0);
    analogWrite(WIND_PIN, 0);
  }

  int distance = abs(analogValue - target);

  static int lastPwmLeft = 0;
  static int lastPwmRight = 0;

  int pwmLeft = 0;
  int pwmRight = 0;
  String direction = "Neutral";

  static bool wasInDeadzone = false;
  bool inDeadzone = distance < DEADZONE;

  if (inDeadzone && !wasInDeadzone && distance < DEADZONE - DEADZONE_HYSTERESIS) {
    wasInDeadzone = true;
  } else if (!inDeadzone && distance > DEADZONE + DEADZONE_HYSTERESIS) {
    wasInDeadzone = false;
  }

  if (wasInDeadzone) {
    if (lastPwmLeft > 0) lastPwmLeft = max(0, lastPwmLeft - MAX_DELTA_PER_LOOP_IDLE);
    if (lastPwmRight > 0) lastPwmRight = max(0, lastPwmRight - MAX_DELTA_PER_LOOP_IDLE);
    pwmLeft = lastPwmLeft;
    pwmRight = lastPwmRight;
    direction = "Neutral";
  } else {
    int effectiveDistance = distance - DEADZONE;
    int maxDistance = 1023 - DEADZONE;
    float x = (float)effectiveDistance / (float)maxDistance;

    float lowCurve = pow(x, .5);    // gentle startup
    float highCurve = pow(x, .1);  // aggressive ramp
    float blend = constrain(x, 0.0f, 1.0f);
    float y = (1.0f - blend) * lowCurve + blend * highCurve;
    y *= 3.0f;

    int pwmValue = (int)round(y * (float)MAX_OUTPUT);
    pwmValue = constrain(pwmValue, 0, MAX_TENTION_VALUE);
    if (pwmValue > 0 && pwmValue < MIN_PWM) pwmValue = MIN_PWM;

    if (analogValue < target) {
      if (stop_clockwise) {
        pwmLeft = 0;
        lastPwmRight = 0;
      } else {
        int delta = constrain(pwmValue - lastPwmLeft, -MAX_DELTA_PER_LOOP_ACTIVE, MAX_DELTA_PER_LOOP_ACTIVE);
        pwmLeft = constrain(lastPwmLeft + delta, 0, MAX_OUTPUT);
        lastPwmLeft = pwmLeft;
        lastPwmRight = max(0, lastPwmRight - MAX_DELTA_PER_LOOP_ACTIVE);
        pwmRight = lastPwmRight;
        direction = "Anticlockwise";
      }
    } else {
      if (stop_antiClockwise) {
        pwmRight = 0;
        lastPwmLeft = 0;
      } else {
        int delta = constrain(pwmValue - lastPwmRight, -MAX_DELTA_PER_LOOP_ACTIVE, MAX_DELTA_PER_LOOP_ACTIVE);
        pwmRight = constrain(lastPwmRight + delta, 0, MAX_OUTPUT);
        lastPwmRight = pwmRight;
        lastPwmLeft = max(0, lastPwmLeft - MAX_DELTA_PER_LOOP_ACTIVE);
        pwmLeft = lastPwmLeft;
        direction = "Clockwise";
      }
    }
  }

  if (FLIP_DIRECTION) {
    analogWrite(WIND_PIN, pwmLeft);
    analogWrite(UNWIDE_PIN, pwmRight);
  } else {
    analogWrite(UNWIDE_PIN, pwmLeft);
    analogWrite(WIND_PIN, pwmRight);
  }

  outputFrames++;
  if (outputFrames >= 250) {
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
    outputFrames = 0;
  }
  delay(4);
}
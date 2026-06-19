#include <Servo.h>
#include <EEPROM.h>

// Firmware version
#define FIRMWARE_VERSION "1.0.0"
#define FIRMWARE_NAME "BBT"
// Wind power PWM (pin 3) value 0-255
uint8_t WindPower = 0;
const int WIND_PIN = 3;

#define LEFT_MOTOR 10
#define RIGHT_MOTOR 9

float L_TARGET = 0;
float R_TARGET = 0;
bool handshakeComplete = false;


Servo ServoLeft, ServoRight;

int L_MIN = 0, R_MIN = 0, L_MAX = 180, R_MAX = 180;
bool L_INVERT = false, R_INVERT = false, DUAL_MOTORS = false;

// Track last time a data was received
unsigned long lastDataTime = 0;

//SLOW is going to be for powering up or powering down
//this will stop fast motor changes
//it is implmented in a way so that it wont break if
//the old firmware is run
bool slow = true;


int ABS_STRENGTH = 20;
bool ABS_ACTIVATED = true;
float ABS_FRQ = 2;
float abs_frame = 0;
float L_ABS, R_ABS;

float _fromSlowModeStart_L = 0;
float _fromSlowModeStart_R = 0;
byte ABS_RUNNING_FRAME = 0;

bool _isConnected = false;
bool _isDisconecting = false;

#define LENGHTOFSLOWTIMEOUT 2000
long slowTimeout = 0;
// Save settings to EEPROM
void saveSettings() {
  int addr = 200;
  EEPROM.put(addr, L_MIN);
  addr += sizeof(L_MIN);
  EEPROM.put(addr, L_MAX);
  addr += sizeof(L_MAX);
  EEPROM.put(addr, R_MIN);
  addr += sizeof(R_MIN);
  EEPROM.put(addr, R_MAX);
  addr += sizeof(R_MAX);
  EEPROM.put(addr, L_INVERT);
  addr += sizeof(L_INVERT);
  EEPROM.put(addr, R_INVERT);
  addr += sizeof(R_INVERT);
  EEPROM.put(addr, DUAL_MOTORS);
}

// Load settings from EEPROM
void loadSettings() {
  int addr = 200;
  EEPROM.get(addr, L_MIN);
  addr += sizeof(L_MIN);
  EEPROM.get(addr, L_MAX);
  addr += sizeof(L_MAX);
  EEPROM.get(addr, R_MIN);
  addr += sizeof(R_MIN);
  EEPROM.get(addr, R_MAX);
  addr += sizeof(R_MAX);
  EEPROM.get(addr, L_INVERT);
  addr += sizeof(L_INVERT);
  EEPROM.get(addr, R_INVERT);
  addr += sizeof(R_INVERT);
  EEPROM.get(addr, DUAL_MOTORS);
}


void setup() {
  Serial.begin(9600);
  pinMode(WIND_PIN, OUTPUT);
  analogWrite(WIND_PIN, WindPower);

  digitalWrite(LEFT_MOTOR, LOW);
  digitalWrite(RIGHT_MOTOR, LOW);


  loadSettings();

  ResetMotors();

  // Fast PWM mode on Timer2
  TCCR2A = _BV(WGM20) | _BV(WGM21) | _BV(COM2B1);  
  TCCR2B = _BV(CS20);  // prescaler = 1 → 31.37 kHz

  Serial.println("Waiting for handshake... Send 'HELLO' to begin.");
}

int outputFrames = 0;


unsigned long lastUpdate = 0;  // when the last update happened


long TotalElapsed = 0;

void SetUPServos() {
  ServoLeft.attach(LEFT_MOTOR);
  ServoRight.attach(RIGHT_MOTOR);
  if (L_INVERT)
    L_TARGET = L_MAX;
  else
    L_TARGET = L_MIN;

  if (R_INVERT)
    R_TARGET = R_MAX;
  else
    R_TARGET = R_MIN;


  // Map 0–180 range to 1000–2000 µs pulse width
  int pulseL = map((int)L_TARGET, 0, 180, 500, 2500);
  int pulseR = map((int)R_TARGET, 0, 180, 500, 2500);

  ServoLeft.writeMicroseconds(pulseL);

  if (DUAL_MOTORS)
    ServoRight.writeMicroseconds(pulseR);
}


void ResetMotors() {

  ABS_ACTIVATED = false;
  if (L_INVERT)
    L_TARGET = L_MAX;
  else
    L_TARGET = L_MIN;

  if (R_INVERT)
    R_TARGET = R_MAX;
  else
    R_TARGET = R_MIN;
}

void DisconectServos() {
  ResetMotors();
  ServoLeft.detach();
  ServoRight.detach();
}


void ProcessSerial() {

  // --- 1) BINARY HANDSHAKE (must be checked BEFORE packet loop) ---
  if (Serial.available() >= 3) {
    uint8_t key = Serial.peek();   // look but don't consume yet

    if (key == 0x00) {
      // Now consume the handshake bytes
      key = Serial.read();
      uint8_t lo = Serial.read();
      uint8_t hi = Serial.read();

      // You can validate lo/hi if you want, but not required
      if (!handshakeComplete) {
        handshakeComplete = true;
        SetUPServos();
        Serial.println("READY");
        _isConnected = true;
        _isDisconecting = false;
      }

      return;  // handshake handled, exit immediately
    }
  }

  // --- 2) NORMAL BINARY PACKETS ---
  while (Serial.available() >= 3) {
    uint8_t key = Serial.read();
    uint8_t lo = Serial.read();
    uint8_t hi = Serial.read();

    int16_t value = (hi << 8) | lo;

    lastDataTime = millis();

    switch (key) {
      case 0x01:
        if (value >= L_MIN && value <= L_MAX)
          L_TARGET = value;
        break;

      case 0x02:
        if (value >= R_MIN && value <= R_MAX)
          R_TARGET = value;
        break;

      case 0x03:
        value = constrain(value, 0, 255);
        WindPower = (uint8_t)value;
        analogWrite(WIND_PIN, WindPower);
        break;

      case 0x04:
        ABS_STRENGTH = value;
        ABS_ACTIVATED = true;
        ABS_RUNNING_FRAME = 0;
        break;

      case 0x05:
        slow = true;
        _fromSlowModeStart_L = L_ABS;
        _fromSlowModeStart_R = R_ABS;
        slowTimeout = LENGHTOFSLOWTIMEOUT;
        break;

      case 0x06:
        slow = false;
        break;

      case 0x10:
        Serial.print("VER:");
        Serial.print(FIRMWARE_NAME);
        Serial.print("-");
        Serial.println(FIRMWARE_VERSION);
        break;

      case 0x11:
        Serial.print("S");
        Serial.print(L_MIN); Serial.print("\t");
        Serial.print(L_MAX); Serial.print("\t");
        Serial.print(R_MIN); Serial.print("\t");
        Serial.print(R_MAX); Serial.print("\t");
        Serial.print(L_INVERT); Serial.print("\t");
        Serial.print(R_INVERT); Serial.print("\t");
        Serial.print(DUAL_MOTORS);
        Serial.println();
        break;

      case 0x12:
      {
        static uint8_t snIndex = 0;

        switch (snIndex) {
          case 0: L_MIN = value; break;
          case 1: L_MAX = value; break;
          case 2: R_MIN = value; break;
          case 3: R_MAX = value; break;
          case 4: L_INVERT = (value != 0); break;
          case 5: R_INVERT = (value != 0); break;
          case 6: DUAL_MOTORS = (value != 0); break;
        }

        snIndex++;
        if (snIndex >= 7) {
          snIndex = 0;
          saveSettings();
        }
      }
      break;

      default:
        break;
    }
  }
}


float lerp(float a, float b, float amount) {
  if (amount < 0.0f) amount = 0.0f;
  if (amount > 1.0f) amount = 1.0f;

  return a + (b - a) * amount;
}

int lastLPos, lastRPos;

void loop() {
  unsigned long now = millis();  // current time in ms
                                 // time since last update
  unsigned long elapsed = now - lastUpdate;
  lastUpdate = now;
  TotalElapsed += elapsed;

  if (slow) {
    slowTimeout -= elapsed;
    if (slowTimeout <= 0) {
      slow = false;
      slowTimeout = 0;

      if (_isDisconecting) {
        _isDisconecting = false;
        _isConnected = false;
      }
    }
  }

  ProcessSerial();

  if (!handshakeComplete) return;

  if (!_isConnected) return;

  if (millis() - lastDataTime > 5000) {
    if (!_isDisconecting) {
      _isDisconecting = true;
      slow = true;
      slowTimeout = LENGHTOFSLOWTIMEOUT;
      _fromSlowModeStart_L = L_ABS;
      _fromSlowModeStart_R = R_ABS;
      ResetMotors();
      // stop wind if no data
      WindPower = 0;
      analogWrite(WIND_PIN, 0);
    }
  }


  L_ABS = 0;
  R_ABS = 0;

  if (!_isDisconecting) {
    if (ABS_ACTIVATED) {
      ABS_RUNNING_FRAME++;
      if (ABS_RUNNING_FRAME > 3)
        ABS_ACTIVATED = false;
      if (abs_frame > ABS_FRQ) {
        if (L_INVERT)
          L_ABS = -ABS_STRENGTH;
        else
          L_ABS = ABS_STRENGTH;


      } else {
        if (R_INVERT)
          R_ABS = -ABS_STRENGTH;
        else
          R_ABS = ABS_STRENGTH;
      }
    }
    // L_ABS = 0;
    L_ABS += L_TARGET;
    R_ABS += R_TARGET;
    abs_frame++;
    if (abs_frame >= ABS_FRQ * 2)
      abs_frame = 0;
    if (L_ABS > L_MAX)
      L_ABS = L_MAX;

    if (L_ABS < L_MIN)
      L_ABS = L_MIN;

    if (R_ABS > R_MAX)
      R_ABS = R_MAX;

    if (R_ABS < R_MIN)
      R_ABS = R_MIN;
  }

  // Map 0–180 range to 1000–2000 µs pulse width
  int pulseL = 0;
  int pulseR = 0;


  //slow = true;
  if (slow)  //slow down motors moving to new position. do this for when powering up or powering down
  {

    float a = (float)slowTimeout / (float)LENGHTOFSLOWTIMEOUT;

    pulseL = lerp(L_TARGET, _fromSlowModeStart_L, a);
    pulseR = lerp(R_TARGET, _fromSlowModeStart_R, a);

    pulseL = map((int)pulseL, 0, 180, 500, 2500);
    pulseR = map((int)pulseR, 0, 180, 500, 2500);


  } else {
    pulseL = map((int)L_ABS, 0, 180, 500, 2500);
    pulseR = map((int)R_ABS, 0, 180, 500, 2500);
  }

  ServoLeft.writeMicroseconds(pulseL);

  if (DUAL_MOTORS)
    ServoRight.writeMicroseconds(pulseR);

  delay(4);
}
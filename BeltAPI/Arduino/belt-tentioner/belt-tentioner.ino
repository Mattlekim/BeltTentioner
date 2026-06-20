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

int L_TARGET = 0;
int R_TARGET = 0;
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
int_fast16_t abs_frame = 0;


float _fromSlowModeStart_L = 0;
float _fromSlowModeStart_R = 0;
byte ABS_RUNNING_FRAME = 0;

bool _isConnected = false;
bool _isDisconecting = false;

#define LENGHTOFSLOWTIMEOUT 3000
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

int last_TargetL, last_TargetR;

void SetUPServos() {
  ServoLeft.attach(LEFT_MOTOR);
  ServoRight.attach(RIGHT_MOTOR);
  ResetMotors();
  last_TargetL = L_TARGET;
  last_TargetR = R_TARGET;

  // Map 0–180 range to 1000–2000 µs pulse width
  int pulseL = map((int)L_TARGET, 0, 180, 500, 2500);
  int pulseR = map((int)R_TARGET, 0, 180, 500, 2500);

  ServoLeft.writeMicroseconds(pulseL);

  if (DUAL_MOTORS)
    ServoRight.writeMicroseconds(pulseR);
}

void EnableSlowMode() {
  slow = true;
  slowTimeout = LENGHTOFSLOWTIMEOUT;

   Serial.println("Slow Mode Started");
}

void setup() {
  Serial.begin(9600);
  pinMode(WIND_PIN, OUTPUT);
  analogWrite(WIND_PIN, WindPower);



  loadSettings();

 // SetUPServos();
  EnableSlowMode();

  Serial.println("Waiting for handshake... Send 'HELLO' to begin.");
}

int outputFrames = 0;


unsigned long lastUpdate = 0;  // when the last update happened


long TotalElapsed = 0;





void DisconectServos() {
  ResetMotors();
  ServoLeft.detach();
  ServoRight.detach();
}




void ProcessSerial() {

  // --- 1) Handshake (only before handshakeComplete) ---
  if (!handshakeComplete && Serial.available() >= 3) {

    uint8_t key = Serial.peek();  // look at first byte

    if (key == 0x00) {
      // Consume handshake bytes
      Serial.read();               // key
      uint8_t lo = Serial.read();  // unused
      uint8_t hi = Serial.read();  // unused

      handshakeComplete = true;
      SetUPServos();
      EnableSlowMode();
      Serial.println("READY");

      _isConnected = true;
      _isDisconecting = false;
      lastDataTime = millis();
      return;  // handshake handled
    }
  }

  // If handshake not done, do not parse packets
  if (!handshakeComplete)
    return;

  // --- 2) Normal packets ---
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
        WindPower = value;
        analogWrite(WIND_PIN, value);
        break;

      case 0x04:
        ABS_STRENGTH = value;
        ABS_ACTIVATED = true;
        ABS_RUNNING_FRAME = 0;
        break;

      case 0x05:
        EnableSlowMode();
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
        Serial.print(L_MIN);
        Serial.print("\t");
        Serial.print(L_MAX);
        Serial.print("\t");
        Serial.print(R_MIN);
        Serial.print("\t");
        Serial.print(R_MAX);
        Serial.print("\t");
        Serial.print(L_INVERT);
        Serial.print("\t");
        Serial.print(R_INVERT);
        Serial.print("\t");
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
    }
  }
}


float lerp(float a, float b, float amount) {
  if (amount < 0.0f) amount = 0.0f;
  if (amount > 1.0f) amount = 1.0f;

  return a + (b - a) * amount;
}




int signInt(int x) {
  return (x > 0) - (x < 0);
}


void loop() {

  last_TargetL = L_TARGET;
  last_TargetR = R_TARGET;


  unsigned long now = millis();  // current time in ms
                                 // time since last update
  unsigned long elapsed = now - lastUpdate;
  lastUpdate = now;
  TotalElapsed += elapsed;
 
  ProcessSerial();

  if (!handshakeComplete) return;

  if (!_isConnected) return;

  if (!_isDisconecting) {
    if (millis() - lastDataTime > 5000) {

      _isDisconecting = true;

      // 1. Reset targets FIRST
     

      EnableSlowMode();
    slowTimeout = 5000;
      // 5. Stop wind
      WindPower = 0;
      analogWrite(WIND_PIN, 0);
    }
  }

  if (_isDisconecting)
   ResetMotors();

  int abs_l = 0, abs_r = 0;
  if (!_isDisconecting) {

    //abs code
    if (ABS_ACTIVATED) {
      ABS_RUNNING_FRAME++;
      if (ABS_RUNNING_FRAME > 3)
        ABS_ACTIVATED = false;
      if (abs_frame > ABS_FRQ) {
        if (L_INVERT)
          abs_l = -ABS_STRENGTH;
        else
          abs_l = ABS_STRENGTH;


      } else {
        if (R_INVERT)
          abs_r = -ABS_STRENGTH;
        else
          abs_r = ABS_STRENGTH;
      }
    }


    abs_frame++;
    if (abs_frame >= ABS_FRQ * 2)
      abs_frame = 0;
  }


 

  if (slow) {
    slowTimeout -= elapsed;
    if (slowTimeout <= 0) {
      slow = false;
      slowTimeout = 0;
      Serial.println("Slow Mode Stopeed");

      if (_isDisconecting)
      {
        _isConnected = false;
        _isDisconecting = false;
      }
    }

    int maxSlewRate = 1;

    int diff_l = L_TARGET - last_TargetL;
    int diff_r = R_TARGET - last_TargetR;

    if (abs(diff_l) > maxSlewRate)
      diff_l = maxSlewRate * signInt(diff_l);

    if (abs(diff_r) > maxSlewRate)
      diff_r = maxSlewRate * signInt(diff_r);

    L_TARGET = last_TargetL + diff_l;
    R_TARGET = last_TargetR + diff_r;
  }




  // Map 0–180 range to 1000–2000 µs pulse width
  int pulseL = map(L_TARGET + abs_l, 0, 180, 500, 2500);
  int pulseR = map(R_TARGET + abs_r, 0, 180, 500, 2500);




  ServoLeft.writeMicroseconds(pulseL);

  if (DUAL_MOTORS)
    ServoRight.writeMicroseconds(pulseR);

  delay(4);
}
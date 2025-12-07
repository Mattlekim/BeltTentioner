#include <Servo.h>
#include <EEPROM.h>


#define POTENIOMETER_PIN A7

#define LEFT_MOTOR 3
#define RIGHT_MOTOR 9

#define MAX_OUTPUT 255


float L_TARGET = 0;
float R_TARGET = 0;
bool handshakeComplete = false;


Servo ServoLeft, ServoRight;

int L_MIN = 0, R_MIN = 0, L_MAX = 180, R_MAX = 180;
bool L_INVERT = false, R_INVERT = false, DUAL_MOTORS = false;

// Save settings to EEPROM
void saveSettings() {
  int addr = 0;
  EEPROM.put(addr, L_MIN);      addr += sizeof(L_MIN);
  EEPROM.put(addr, L_MAX);      addr += sizeof(L_MAX);
  EEPROM.put(addr, R_MIN);      addr += sizeof(R_MIN);
  EEPROM.put(addr, R_MAX);      addr += sizeof(R_MAX);
  EEPROM.put(addr, L_INVERT);   addr += sizeof(L_INVERT);
  EEPROM.put(addr, R_INVERT);   addr += sizeof(R_INVERT);
  EEPROM.put(addr, DUAL_MOTORS);

}

// Load settings from EEPROM
void loadSettings() {
  int addr = 0;
  EEPROM.get(addr, L_MIN);      addr += sizeof(L_MIN);
  EEPROM.get(addr, L_MAX);      addr += sizeof(L_MAX);
  EEPROM.get(addr, R_MIN);      addr += sizeof(R_MIN);
  EEPROM.get(addr, R_MAX);      addr += sizeof(R_MAX);
  EEPROM.get(addr, L_INVERT);   addr += sizeof(L_INVERT);
  EEPROM.get(addr, R_INVERT);   addr += sizeof(R_INVERT);
  EEPROM.get(addr, DUAL_MOTORS);

}


void setup() {
  Serial.begin(9600);


  ServoLeft.attach(LEFT_MOTOR);
  ServoRight.attach(RIGHT_MOTOR);

  loadSettings();
  Serial.println("Waiting for handshake... Send 'HELLO' to begin.");

  ServoLeft.write(0);
  ServoRight.write(0);
}

int outputFrames = 0;


unsigned long lastUpdate = 0;  // when the last update happened


long TotalElapsed = 0;

void ProcessSerial() {
  while (Serial.available() > 0) {
    String input = Serial.readStringUntil('\n');
    input.trim();
    if (input.length() == 0) continue;



    if (input.equalsIgnoreCase("HELLO")) {
      if (!handshakeComplete) {
        handshakeComplete = true;
        Serial.println("READY");
      }

    } else if (input.equalsIgnoreCase("SETTINGS")) {
      handshakeComplete = true;


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

    } else if (input.startsWith("SN:")) {
      String inputLine = input.substring(3);
      String parts[7];
      int count = 0;

      int start = 0;
      int idx = inputLine.indexOf("-");  // literal backslash + t
      while (idx != -1 && count < 6) {
        parts[count++] = inputLine.substring(start, idx);
        start = idx + 1;  // skip over "\t"
        idx = inputLine.indexOf("-", start);
      }
      parts[count++] = inputLine.substring(start);

      // Now parse
      L_MIN = parts[0].toInt();
      L_MAX = parts[1].toInt();
      R_MIN = parts[2].toInt();
      R_MAX = parts[3].toInt();
      L_INVERT = parts[4].toInt() != 0;
      R_INVERT = parts[5].toInt() != 0;
      DUAL_MOTORS = parts[6].toInt() != 0;

      
      saveSettings();
    } else {
      int separatorIndex = input.indexOf(':');
      if (separatorIndex > 0) {
        String key = input.substring(0, separatorIndex);
        String valueStr = input.substring(separatorIndex + 1);
        float value = valueStr.toFloat();
        if (key == "L" && value >= L_MIN && value <= L_MAX) {
          L_TARGET = value;
        }

        if (key == "R" && value >= R_MIN && value <= R_MAX) {
          R_TARGET = value;
        }
      }
    }
  }
}

void loop() {
  unsigned long now = millis();  // current time in ms
                                 // time since last update
  unsigned long elapsed = now - lastUpdate;
  lastUpdate = now;
  TotalElapsed += elapsed;
  //analogWrite(UNWIDE_PIN, 200);
  //analogWrite(WIND_PIN, 200);
  //return;

  ProcessSerial();

  // if (TotalElapsed > 1000) {
  //  Serial.print("NC--");
  // Serial.println(L_TARGET);
  //   TotalElapsed = 0;
  // }
  if (!handshakeComplete) return;

  ServoLeft.write(L_TARGET);




  delay(4);
}
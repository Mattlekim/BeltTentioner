using BeltAPI;
using System.Timers;

bool connected = false;
bool alive = true;

bool duelMotors = true;
MotorSettings _motorSettings = new MotorSettings();
bool haveMotorSettings = false;

bool refreshDisplay = true;

BeltTentionerExample.TestState testState = BeltTentionerExample.TestState.Idle;

CarSettings _carSettings = new CarSettings();
_carSettings.SwayStrength = 50;
_carSettings.SurgeStrenght = 50;
_carSettings.HeaveStrength = 50;
_carSettings.SurgeCurveAmount = 1;
_carSettings.SwayCurveAmount = 1;
_carSettings.MaxPower = 100;
_carSettings.RestingPoint = 0;
_carSettings.NegativeSway = 0f;
_carSettings.RestingPoint = 0;
float testValue = 0;



BeltSerialDevice bsd = new BeltSerialDevice();
bool enableSlowMode = false;
var serialSendDataTimer = new System.Timers.Timer(33);
bool skip = false;
serialSendDataTimer.Elapsed += (s, e) =>
{
  //  System.Diagnostics.Debugger.Break();
    if (!connected)
        return;
    if (!haveMotorSettings)
        return;

    if (skip)
        return;
    skip = true;

    BeltMotorData bmd;
    float output = 0;
    Console.SetCursorPosition(30, 9);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write($"{testState}");
    Console.ForegroundColor = ConsoleColor.White;
    switch (testState)
    {
        case BeltTentionerExample.TestState.Idle:
            bmd = _motorSettings.Setup(0, 0, 1, _carSettings);
            bmd.SendDataToSerial(bsd, _carSettings);
            break;
        case BeltTentionerExample.TestState.TestSurge:
            testValue += 0.02f;

            output = Math.Abs((float)(Math.Sin(testValue) * 7f));

            bmd = bsd.SetupMotorsForData(output, 0, 1, _carSettings);
            bmd.SendDataToSerial(bsd, _carSettings);

           
            
            Console.SetCursorPosition(30, 10);
            Console.Write("Surge Output: " + output.ToString("0.00") + "    ");
            break;
        case BeltTentionerExample.TestState.TestSway:
            testValue += 0.02f;
            output = (float)(Math.Sin(testValue) * 5f);

            bmd = bsd.SetupMotorsForData(0, output, 1, _carSettings);
            bmd.SendDataToSerial(bsd, _carSettings);

            Console.SetCursorPosition(30, 10);
            Console.Write("Sway Output: " + output.ToString("0.00") + "    ");
            break;
        case BeltTentionerExample.TestState.TestHeave:
            testValue += 0.02f;
            output = (float)(Math.Sin(testValue) * 3f);

            bmd = bsd.SetupMotorsForData(0, 0, output + 1, _carSettings);
            bmd.SendDataToSerial(bsd, _carSettings);

            Console.SetCursorPosition(30, 10);
            Console.Write("Heave Output: " + output.ToString("0.00") + "    ");
            break;
        case BeltTentionerExample.TestState.TestLeft:
            testValue += 0.02f;
            output = (float)(Math.Sin(testValue) * 3f);
            bmd = bsd.SetupMotorsForData(0, -Math.Abs(output), 1, _carSettings);

            bmd.SendDataToSerial(bsd, _carSettings);

            Console.SetCursorPosition(30, 10);
            Console.Write("Left Output: " + Math.Abs(output).ToString("0.00") + "    ");
            break;

        case BeltTentionerExample.TestState.TestRight:
            testValue += 0.02f;
            output = (float)(Math.Sin(testValue) * 3f);
            bmd = bsd.SetupMotorsForData(0, Math.Abs(output), 1, _carSettings);

            bmd.SendDataToSerial(bsd, _carSettings);

            Console.SetCursorPosition(30, 10);
            Console.Write("Right Output: " + Math.Abs(output).ToString("0.00") + "    ");
            break;

        case BeltTentionerExample.TestState.TestMin:

            bmd = bsd.SetupMotorsForData(0, 0, 0, _carSettings);

            bmd.SendDataToSerial(bsd, _carSettings);

            Console.SetCursorPosition(30, 10);
            Console.Write("Setting Motors To Minium angle");
            break;


        case BeltTentionerExample.TestState.TestMax:
            CarSettings tmpsettings = _carSettings.DeepCopy();
            tmpsettings.MaxPower = 100;
            tmpsettings.SurgeStrenght = 100;
            bmd = bsd.SetupMotorsForData(200, 0, 0, tmpsettings);

            bmd.SendDataToSerial(bsd, tmpsettings);

            Console.SetCursorPosition(30, 10);
            Console.Write("Setting Motors To Maximum angle");
            break;

        case BeltTentionerExample.TestState.TestSlowMode:
            if (enableSlowMode)
            {
                testValue = 20f;
                
                bmd = bsd.SetupMotorsForData(testValue, 0, 1, _carSettings);
                bmd.SendDataToSerial(bsd, _carSettings);
                Console.SetCursorPosition(30, 10);
                Console.Write("Setting Motor Position");
                

                Thread.Sleep(2000);
                bsd.SendSlowMode();
                Thread.Sleep(100);
                enableSlowMode = false;
                break; ;
            }
            testValue = 0f;
            bmd = bsd.SetupMotorsForData(testValue, 0, 1, _carSettings);
            bmd.SendDataToSerial(bsd, _carSettings);
            Console.SetCursorPosition(30, 10);
            Console.Write("Slow Mode Enabled");
            break;
    }
    skip = false;

};
serialSendDataTimer.Start();
serialSendDataTimer.AutoReset = true;

Console.SetCursorPosition(0, 0);
Console.WriteLine($"Attempting Connection");



bsd.OnMotorSettingsRecived = () =>
{
    _motorSettings = bsd.DeviceMotorSettings;
    duelMotors = bsd.DuelMotors;
    
    haveMotorSettings = true;
    refreshDisplay = true;
};
CancellationToken ct = new CancellationToken();
connected = await bsd.ConnectAsync(ct);

if (!connected)
{
    
    Console.WriteLine($"Connection Failed!!");
   
    serialSendDataTimer.Stop();
    Thread.Sleep(1000);
    return;
}

Console.CursorVisible = false;
while (alive)
{
    if (refreshDisplay)
    {
        Console.Clear();
        refreshDisplay = false;

        Console.SetCursorPosition(0, 0);
        Console.WriteLine($"Connected: {connected}");

        Console.SetCursorPosition(0, 3);
        Console.WriteLine($"Belt Tention Example Project");

        if (haveMotorSettings)
        {
            Console.SetCursorPosition(0, 5);
            Console.WriteLine($"Motor Settings Recived:");
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.SetCursorPosition(40, 6);
            Console.WriteLine($"Duel Motors: {duelMotors}");
            Console.SetCursorPosition(40, 0);
            Console.WriteLine($"Left Motor MinAngle: {_motorSettings.LeftMinimumAngle}");
            Console.SetCursorPosition(40, 1);
            Console.WriteLine($"Left Motor MaxAngle: {_motorSettings.LeftMaximumAngle}");
            Console.SetCursorPosition(40, 2);
            Console.WriteLine($"Left Motor Inverted: {_motorSettings.LeftInverted}");

            Console.SetCursorPosition(40, 3);
            Console.WriteLine($"Right Motor MinAngle: {_motorSettings.RightMinimumAngle}");
            Console.SetCursorPosition(40, 4);
            Console.WriteLine($"Right Motor MaxAngle: {_motorSettings.RightMaximumAngle}");
            Console.SetCursorPosition(40, 5);
            Console.WriteLine($"Right Motor Inverted: {_motorSettings.RightInverted}");
            Console.BackgroundColor = ConsoleColor.DarkRed;


            Console.SetCursorPosition(70, 0);
            Console.WriteLine("Car Settings:");
            Console.SetCursorPosition(70, 1);
            Console.WriteLine($"Surge Strength: {_carSettings.SurgeStrenght}");
            Console.SetCursorPosition(70, 2);
            Console.WriteLine($"Sway Strength: {_carSettings.SwayStrength}");
            Console.SetCursorPosition(70, 3);
            Console.WriteLine($"Heave Strength: {_carSettings.HeaveStrength}");
            Console.SetCursorPosition(70, 4);
            Console.WriteLine($"Surge Curve: {_carSettings.SurgeCurveAmount}");
            Console.SetCursorPosition(70, 5);
            Console.WriteLine($"Sway Curve: {_carSettings.SwayCurveAmount}");
            Console.SetCursorPosition(70, 6);
            Console.WriteLine($"Max Power: {_carSettings.MaxPower}");
            Console.SetCursorPosition(70, 7);
            Console.WriteLine($"Resting Point: {_carSettings.RestingPoint}");
            


            Console.BackgroundColor = ConsoleColor.Black;
        }
        else
        {
            Console.SetCursorPosition(0, 5);
            Console.WriteLine($"Waiting On Motor Settings");
        }

        Console.SetCursorPosition(0, 7);
        Console.WriteLine("1: Test Surge");
        Console.SetCursorPosition(0, 8);
        Console.WriteLine("2: Test Sway");
        Console.SetCursorPosition(0, 9);
        Console.WriteLine("3: Test Heave");
        Console.SetCursorPosition(0, 10);
        Console.WriteLine("4: Test Left");
        Console.SetCursorPosition(0, 11);
        Console.WriteLine("5: Test Right");
        Console.SetCursorPosition(0, 12);
        Console.WriteLine("6: Test Min");
        Console.SetCursorPosition(0, 13);
        Console.WriteLine("7: Test Max");
        Console.SetCursorPosition(0, 14);
        Console.WriteLine("8: Slow Mode Test");

        Console.SetCursorPosition(0, 15);
        Console.WriteLine("9: Stop Test");
        Console.SetCursorPosition(0, 16);
        Console.WriteLine("0: Exit");

    }
    if (Console.KeyAvailable)
    {
        var keyInfo = Console.ReadKey(intercept: true);
        Console.Clear();

        refreshDisplay = true;
        switch (keyInfo.Key)
        {
            case ConsoleKey.D1:
                testState = BeltTentionerExample.TestState.TestSurge;
                break;
            case ConsoleKey.D2:
                testState = BeltTentionerExample.TestState.TestSway;
                // bsd.TestRightMotor();
                break;
            case ConsoleKey.D3:
                testState = BeltTentionerExample.TestState.TestHeave;
                // bsd.TestBothMotors();
                break;
           
            case ConsoleKey.D4:
                testState = BeltTentionerExample.TestState.TestLeft;
               


                break;
            case ConsoleKey.D5:
                testState = BeltTentionerExample.TestState.TestRight;


                break;

            case ConsoleKey.D6:
                testState = BeltTentionerExample.TestState.TestMin;


                break;

            case ConsoleKey.D7:
                testState = BeltTentionerExample.TestState.TestMax;


                break;

            case ConsoleKey.D8:
                testState = BeltTentionerExample.TestState.TestSlowMode;
                enableSlowMode = true;

                break;

            case ConsoleKey.D9:
                testState = BeltTentionerExample.TestState.Idle;

                break;


            case ConsoleKey.D0:
                alive = false;
                serialSendDataTimer.Stop();
                

                break;
        }
    }
    else
        Thread.Sleep(100);
}

// Keep application alive if needed
// Thread.Sleep(Timeout.Infinite);
open Trik
open Trik.Helpers
open System.Threading

let maxAngleX = 40
let maxAngleY = 30
let scaleConst = 10
let RGBdepth = 255.
let SVscale = 100.
let minMass = 5
let deepPink = (255, 20, 147)

let scale var mA  = (Trik.Helpers.limit (-mA) mA var) / scaleConst

let updatePositionX x acc = 
    if (x > 15 && x <= 100 && acc < 90) || (x < - 15 && x >= -100 && acc > -90) 
    then acc + scale x maxAngleX
    else acc

let updatePositionY y acc =
    if (y > 10 && y <= 100 && acc < 30) || (y < - 10 && y >= -100 && acc > -20) 
    then acc + scale y maxAngleY
    else acc

let conversion (x : DetectTarget) = 
    let (r, g, b) = HSVtoRGB(float x.Hue, (float x.Saturation) / SVscale, (float x.Value) / SVscale)
    (int (r * RGBdepth), int (g * RGBdepth), int (b * RGBdepth))

let exit = new EventWaitHandle(false, EventResetMode.AutoReset)

[<EntryPoint>]
let main _ =
    let model = new Model(ObjectSensorConfig = Ports.VideoSource.USB)
    model.ServoConfig.[0] <- ("E1", "/sys/class/pwm/ehrpwm.1:1", { stop = 0; zero = 1600000; min = 800000; max = 2400000; period = 20000000 })
    model.ServoConfig.[1] <- ("E2", "/sys/class/pwm/ehrpwm.1:0", { stop = 0; zero = 1600000; min = 800000; max = 2400000; period = 20000000 })
    Helpers.SendToShell """v4l2-ctl -d "/dev/video2" --set-ctrl white_balance_temperature_auto=1""" //option for better color reproduction

    let sensor = model.ObjectSensor
    let buttons = new ButtonPad()

    buttons.Start()
    sensor.Start()
    model.LedStripe.SetPower deepPink

    let sensorOutput = sensor.ToObservable()
    
    let targetStream = sensorOutput |> Observable.choose (fun o -> o.TryGetTarget) 

    use setterDisposable = targetStream |> Observable.subscribe sensor.SetDetectTarget

    let colorStream = targetStream |> Observable.map (fun x -> conversion x)

    use ledstripeDisposable = colorStream.Subscribe model.LedStripe

    let powerSetterDisposable = 
             model.ObjectSensor.ToObservable()  
             |> Observable.choose (fun o -> o.TryGetLocation)
             |> Observable.filter (fun loc -> loc.Mass > minMass) 
             |> Observable.scan (fun (accX, accY) loc -> (updatePositionX loc.X accX, updatePositionY loc.Y accY)) (0, 0)
             |> Observable.subscribe (fun (a, b) -> model.Servo.["E1"].SetPower -a
                                                    model.Servo.["E2"].SetPower -b)

    use downButtonDispose = buttons.ToObservable() 
                            |> Observable.filter (fun x -> ButtonEventCode.Down = x.Button) 
                            |> Observable.subscribe (fun _ -> sensor.Detect())
    
    use upButtonDispose = buttons.ToObservable()
                          |> Observable.filter (fun x -> ButtonEventCode.Up = x.Button)
                          |> Observable.subscribe (fun _ -> exit.Set() |> ignore)

    exit.WaitOne() |> ignore
    0

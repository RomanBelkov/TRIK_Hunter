open Trik
open System.Threading

let maxAngleX = 40
let maxAngleY = 60
let scaleConstX = 10
let scaleConstY = 20
let RGBdepth = 255.
let SVscale = 100.
let minMass = 5
let deepPink = (255, 20, 147)

let scale var mA sC  = (Trik.Helpers.limit (-mA) mA var) / sC

let updatePositionX x acc = 
    if (x > 15 && x <= 100 && acc < 90) || (x < -15 && x >= -100 && acc > -90) 
    then acc + scale x maxAngleX scaleConstX
    else acc

let updatePositionY y acc =
    if (y > 5 && y <= 100 && acc < 20) || (y < -5 && y >= -100 && acc > -40) 
    then acc + scale y maxAngleY scaleConstY
    else acc

let conversion (x : DetectTarget) = 
    let h = Trik.Helpers.limit 0.9 1.
    let (r, g, b) = Trik.Helpers.HSVtoRGB(float x.Hue, h((float x.Saturation) / 100.) , h((float x.Value) / 100.))
//    printfn "HUE: %d %d %d %d %d %d" x.Hue x.Saturation x.Value x.HueTolerance x.SaturationTolerance x.ValueTolerance
//    printfn "RGB: %d %d %d" (int (r * RGBdepth)) (int (g * RGBdepth)) (int (b * RGBdepth))
    (int (r * RGBdepth), int (g * RGBdepth), int (b * RGBdepth))

let exit = new EventWaitHandle(false, EventResetMode.AutoReset)

[<EntryPoint>]
let main _ =
    let model = new Model(ObjectSensorConfig = Ports.VideoSource.USB)
    model.ServoConfig.[0] <- ("E1", "/sys/class/pwm/ehrpwm.1:1", { stop = 0; zero = 1600000; min = 800000; max = 2400000; period = 20000000 })
    model.ServoConfig.[1] <- ("E2", "/sys/class/pwm/ehrpwm.1:0", { stop = 0; zero = 1600000; min = 800000; max = 2400000; period = 20000000 })

    let sensor = model.ObjectSensor
    let buttons = new ButtonPad()

    buttons.Start()
    sensor.Start()

    Trik.Helpers.SendToShell """v4l2-ctl -d "/dev/video2" --set-ctrl white_balance_temperature_auto=1"""
    model.LedStripe.SetPower deepPink

    let sensorOutput = sensor.ToObservable()
    
    let targetStream = sensorOutput |> Observable.choose (fun o -> o.TryGetTarget) 

    use setterDisposable = targetStream |> Observable.subscribe sensor.SetDetectTarget

    let colorStream = targetStream |> Observable.map conversion

    use ledstripeDisposable = colorStream.Subscribe model.LedStripe

    let powerSetterDisposable = 
             model.ObjectSensor.ToObservable()  
             |> Observable.choose (fun o -> o.TryGetLocation)
             |> Observable.filter (fun loc -> loc.Mass > minMass) 
             |> Observable.scan (fun (accX, accY) loc -> (updatePositionX loc.X accX, updatePositionY loc.Y accY)) (0, 0)
             |> Observable.subscribe (fun (a, b) -> model.Servo.["E1"].SetPower -a
                                                    model.Servo.["E2"].SetPower b)


    use downButtonDispose = buttons.ToObservable() 
                            |> Observable.filter (fun x -> ButtonEventCode.Down = x.Button) 
                            |> Observable.subscribe (fun _ -> sensor.Detect())
    
    use upButtonDispose = buttons.ToObservable()
                          |> Observable.filter (fun x -> ButtonEventCode.Up = x.Button)
                          |> Observable.subscribe (fun _ -> exit.Set() |> ignore)

    use timerSetterDisposable = Observable.Interval(System.TimeSpan.FromSeconds 40.0) 
                                |> Observable.subscribe (fun _ -> sensor.Detect())

    exit.WaitOne() |> ignore
    0

open Trik
open System.Threading

let maxAngleX = 40
let maxAngleY = 60
let scaleConstX = 10
let scaleConstY = 20
let RGBdepth = 100.
let minMass = 5

let red = (100, 0, 0)
let green = (0, 100, 0)
let blue = (0, 0, 100)
let black = (0, 0, 0)
let white = (100, 100, 100)
let brown = (65, 17, 17)
let orange = (100, 65, 0)
let yellow = (100, 100, 0)
let teal = (0, 50, 50)
let purple = (63, 13, 94)
let pink = (100, 75, 80)
let colors = 
    [red; green; blue; (*white;*) (*black;*) brown; orange; yellow; teal; purple; pink]

let scale var mA sC  = (Trik.Helpers.limit (-mA) mA var) / sC

let updatePositionX x acc = 
    if (x > 15 && x <= 100 && acc < 90) || (x < -15 && x >= -100 && acc > -90) 
    then acc + scale x maxAngleX scaleConstX
    else acc

let updatePositionY y acc =
    if (y > 5 && y <= 100 && acc < 20) || (y < -5 && y >= -100 && acc > -40) 
    then acc + scale y maxAngleY scaleConstY
    else acc


let colorProcessor (r, g, b) = 
    printfn "rgb : %d %d %d" (r * 255 / 100) (g * 255 / 100) (b * 255 / 100)
    let del (x, y, z) = (x - r) * (x - r) + (g - y) * (g - y) + (b - z) * (b - z)
    let rec loop (x :: xs) acc =
        let sup = del x
        let accDelta = del acc
        match (x :: xs) with
        | [t] -> printfn "t : %A ;; acc : %A" t acc; if del t < accDelta then t else acc 
        | x :: xs when sup < accDelta -> loop xs x
        | x :: xs when sup >= accDelta -> loop xs acc   
        | _ -> failwith "no way" 
    loop colors red

let conversion (x : DetectTarget) = 
    printfn "%d %d %d %d %d %d" x.Hue x.Saturation x.Value x.HueTolerance x.SaturationTolerance x.ValueTolerance
    let (r, g, b) = 
        Trik.Helpers.HSVtoRGB(float x.Hue, (float x.Saturation) / 100. , (float x.Value) / 100.)
    colorProcessor (int (r * RGBdepth), int (g * RGBdepth), int (b * RGBdepth))

let exit = new EventWaitHandle(false, EventResetMode.AutoReset)

[<EntryPoint>]
let main _ =
    let model = new Model(ObjectSensorConfig = Ports.VideoSource.USB)
    model.ServoConfig.[0] <- ("E1", "/sys/class/pwm/ehrpwm.1:1", { stop = 0; zero = 1600000; min = 800000; max = 2400000; period = 20000000 })
    model.ServoConfig.[1] <- ("E2", "/sys/class/pwm/ehrpwm.1:0", { stop = 0; zero = 1600000; min = 800000; max = 2400000; period = 20000000 })

    let sensor = model.ObjectSensor
    let buttons = new ButtonPad()

    model.LedStripe.SetPower (75, 20, 20)

    let sensorOutput = sensor.ToObservable()
    
    let targetStream = sensorOutput |> Observable.choose (fun o -> o.TryGetTarget) 

    use setterDisposable = targetStream |> Observable.subscribe sensor.SetDetectTarget

    let colorStream = targetStream |> Observable.map conversion

    use ledstripeDisposable = colorStream.Subscribe model.LedStripe

    use powerSetterDisposable = 
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
    
    buttons.Start()
    sensor.Start()
    Trik.Helpers.SendToShell """v4l2-ctl -d "/dev/video2" --set-ctrl white_balance_temperature_auto=1"""


    exit.WaitOne() |> ignore
    0

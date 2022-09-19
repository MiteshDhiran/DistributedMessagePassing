// For more information see https://aka.ms/fsharp-console-apps
open System
open System.Net
open System.Runtime.Serialization.Formatters.Binary

type result<'a> =
    | Failure
    | Success of 'a

let streamConnection (stream: System.IO.Stream) =
    let received = Event<_>()
    async {
    while true do
        let fmt = BinaryFormatter()
        fmt.Deserialize stream
            |> unbox
            |> received.Trigger
    } |> Async.Start
    let send msg =
        try
            let fmt = BinaryFormatter()
            fmt.Serialize(stream, box msg)
            Success()
        with _ ->
            Failure
    send, received.Publish
    
let clientConnection (ipAddress: IPAddress, port) =
    try
        let client = new Sockets.TcpClient(NoDelay=true)
        client.Connect(ipAddress, port)
        let stream = client.GetStream()
        Success(streamConnection stream)
    with _ -> Failure
    

let serverConnection port =
    try
        let server = Sockets.TcpListener(IPAddress.Any, port)
        server.Start()
        let relay = Event<_>()
        let received = Event<_>()
        async {
            while true do
                let client = server.AcceptTcpClient()
                let stream = client.GetStream()
                let sendToClient, receivedFromClient = streamConnection stream
                receivedFromClient.Add received.Trigger
                let rec handler = Handler<_>(fun _ msg ->
                                                match sendToClient msg with
                                                | Success() -> ()
                                                | Failure -> relay.Publish.RemoveHandler handler
                                             )
                relay.Publish.AddHandler handler
        } |> Async.Start
        Success(relay.Trigger, received.Publish)
    with _ -> Failure

type 'a msg =
    | Hello of 'a
    | World of 'a
   
let print s = System.Console.WriteLine((s()).ToString())    

// let ipAddress, port = IPAddress.Parse "192.168.1.5", 8001
let ipAddress, port = IPAddress.Loopback, 8001
print (fun () -> "Connecting to server..." + ipAddress.ToString())

do
    match serverConnection port with
            | Failure -> failwith "Failed to start server"
            | Success(relayToAllClients, receivedFromClient) ->
                    let serverHandler msg =
                        print(fun () -> sprintf "Server received: %A" msg)
                        match msg with
                        | Hello n -> relayToAllClients (World (n+1))
                        | World _ -> ()
                    receivedFromClient.Add serverHandler
                    match clientConnection (ipAddress, port) with
                    | Failure -> failwith "Client failed to connect to server"
                    | Success(sendToServer, receivedFromServer) ->
                            let clientHandler msg =
                                print(fun () -> sprintf "Client received: %A" msg)
                            receivedFromServer.Add clientHandler
                            match sendToServer (Hello 3) with
                            | Failure -> failwith "Client failed to send message to server"
                            | Success() -> ()

print (fun () -> "Done") 
stdin.ReadLine() |> ignore
                    
﻿namespace Fablish
    
[<AutoOpen>]
module Tokens =
    let magic = "This is Aardvark"

    [<Literal>]
    let eventOccurance  = 1
    [<Literal>]
    let forceRendering = 2

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
type Script = 
    | JsLambda of string
    | Ignore

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Scripts =
    let ignore = Ignore

type RenderCallback<'msg> = string -> Option<'msg>
type Reaction<'msg> = { clientSide : Script; serverSide : RenderCallback<'msg>}
type OnRendered<'model,'msg,'view> = 'model -> 'view -> Reaction<'msg>

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module OnRendered =
    let ignore : 'model -> 'view -> Reaction<'msg> = fun _ _ -> { clientSide = Scripts.ignore; serverSide = fun _ -> None }

type Sub<'msg> = 
    | NoSubscription 

type Cmd<'msg> =
    | NoCmd
    | Cmd of Async<'msg>

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Cmd =
    let none = NoCmd

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Sub =
    let none = NoSubscription


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Subscriptions =
    let none = fun _ -> Sub.none

[<AutoOpen>]
module UglyStuff =
    open System
    open System.Collections.Generic
    open Aardvark.Base
    open Aardvark.Base.Incremental

    type Env<'msg> = 
        abstract member Run : Cmd<'msg> -> unit 

    and FablishInstance<'model,'msg>(m : 'model, update : Env<'msg> -> 'model -> 'msg -> 'model) as this =
        let viewers = HashSet<MVar<'model>>()
        let modelSubscriptions = Dictionary<_,_>()
        let messageSubscriptions = Dictionary<_,_>()
        let model = Mod.init m

        let env = 
            { new Env<'msg> with
                member x.Run(cmd) = 
                    match cmd with
                        | NoCmd -> ()
                        | Cmd cmd -> 
                            async {
                                let! msg = cmd
                                this.EmitMessage msg
                            } |> Async.Start
         }

        member x.AddViewer m =
            lock viewers (fun _ -> 
                viewers.Add m |> ignore
                m.Put model.Value
            )
        member x.EmitMessage msg =
            lock viewers (fun _ -> 
                let newModel = update env model.Value msg
                model.Value <- newModel
                for sub in messageSubscriptions.Values do
                    sub msg

                for v in viewers do
                    MVar.put v newModel
            )

        member x.SubscribeModel(f : 'model -> unit) =
            let d = { new IDisposable with member x.Dispose() = lock viewers (fun _ -> modelSubscriptions.Remove x |> ignore) }
            lock viewers (fun _ -> 
                modelSubscriptions.Add(d,f) |> ignore
            )
            d

        member x.SubscribeMessage(f : 'msg -> unit) =
            let d = { new IDisposable with member x.Dispose() = lock viewers (fun _ -> messageSubscriptions.Remove x |> ignore) }
            lock viewers (fun _ -> 
                messageSubscriptions.Add(d,f) |> ignore
            )
            d

        member x.UnsafeCurrentModel = model.Value

    module Env =
        let map (f : 'b -> 'a) (a : Env<'a>) : Env<'b> =
            {
                new Env<'b> with
                    member x.Run b = 
                        match b with
                            | Cmd.NoCmd -> ()
                            | Cmd.Cmd c -> 
                                let f = 
                                    async {
                                        let! c = c
                                        return f c
                                    } |> Cmd.Cmd
                                a.Run f
                                
            }


type App<'model,'msg,'view> = 
    {
        initial       : 'model
        update        :  Env<'msg> -> 'model    -> 'msg -> 'model
        view          : 'model     -> 'view
        subscriptions : 'model     ->  Sub<'msg>

        // IO extensions
        onRendered : OnRendered<'model,'msg,'view>
    }


type Event = { eventId : string; eventValue : string }
type Message = { id : int; data : Event }
type RenderRequest = { dom : string; script : string; id : string }

type Registrations<'msg> = Map<int,(obj->'msg)>
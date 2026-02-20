namespace BrokenRealm.Client

open System.Threading.Tasks
open Blazored.LocalStorage
open BrokenRealm.Common
open FsToolkit.ErrorHandling

[<RequireQualifiedAccess>]
module Persistence =
    
    let private asAsync<'T> (task: ValueTask<'T>) =
        
        task
        |> _.AsTask()
        |> Async.AwaitTask
    
    let private asAsyncUnit (task: ValueTask) =
        
        task
        |> _.AsTask()
        |> Async.AwaitTask
    
    let set<'T> (storage: ILocalStorageService) key value =
        
        storage.SetItemAsync(key, value)
        |> asAsyncUnit

    let tryGet<'T> (storage: ILocalStorageService) key =
        
        storage.ContainKeyAsync(key)
        |> asAsync
        |> Async.bind (
            function
            | false -> None |> Async.returnM
            | true ->
                storage.GetItemAsync<'T>(key)
                |> asAsync
                |> Async.map Some
        )

    let clear (storage: ILocalStorageService) key =
        
        storage.RemoveItemAsync(key)
        |> asAsyncUnit

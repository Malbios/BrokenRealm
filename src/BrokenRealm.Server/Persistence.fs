namespace BrokenRealm.Server

open System

type BehaviorModuleSnapshot =
    { Id: string
      RegistryName: string
      Dependencies: string list
      Source: string
      SourceRevision: int64
      ActivationRevision: int64
      ActivatedAt: DateTimeOffset }

type WorldSnapshot =
    { Revision: int64
      ItemIds: Set<ItemId>
      BehaviorModules: Map<string, BehaviorModuleSnapshot>
      Objects: Map<ObjectId, GameObject> }

type CharacterSnapshot =
    { Id: string
      Revision: int64
      LocationId: ObjectId
      Inventory: Map<ItemId, Quantity> }

type GameSnapshot =
    { FormatVersion: int
      World: WorldSnapshot
      Character: CharacterSnapshot }

type StoredGameState =
    { State: GameState
      WorldRevision: int64
      CharacterRevision: int64 }

type CommitConflict =
    { ExpectedWorldRevision: int64
      ActualWorldRevision: int64
      ExpectedCharacterRevision: int64
      ActualCharacterRevision: int64 }

module GameSnapshots =
    [<Literal>]
    let CurrentFormatVersion = 1

    [<Literal>]
    let PrototypeCharacterId = "prototype-player"

    let private captureBehavior now sourceRevision activationRevision (behaviorModule: BehaviorModule) =
        { Id = behaviorModule.Id
          RegistryName = behaviorModule.RegistryName
          Dependencies = behaviorModule.Dependencies
          Source = behaviorModule.Source
          SourceRevision = sourceRevision
          ActivationRevision = activationRevision
          ActivatedAt = now }

    let create now (state: GameState) =
        let behaviors =
            state.BehaviorModules
            |> Map.map (fun _ behaviorModule -> captureBehavior now 0L 0L behaviorModule)

        { FormatVersion = CurrentFormatVersion
          World =
            { Revision = 0L
              ItemIds = state.ItemIds
              BehaviorModules = behaviors
              Objects = state.Objects }
          Character =
            { Id = PrototypeCharacterId
              Revision = 0L
              LocationId = state.Player.LocationId
              Inventory = state.Player.Inventory } }

    let private updateBehaviors now previous (modules: Map<string, BehaviorModule>) =
        modules
        |> Map.map (fun id behaviorModule ->
            match previous |> Map.tryFind id with
            | Some stored
                when stored.RegistryName = behaviorModule.RegistryName
                     && stored.Dependencies = behaviorModule.Dependencies
                     && stored.Source = behaviorModule.Source ->
                stored
            | Some stored ->
                captureBehavior now (stored.SourceRevision + 1L) (stored.ActivationRevision + 1L) behaviorModule
            | None -> captureBehavior now 0L 0L behaviorModule)

    let update now previous (state: GameState) =
        let behaviorModules = updateBehaviors now previous.World.BehaviorModules state.BehaviorModules
        let worldChanged =
            previous.World.ItemIds <> state.ItemIds
            || previous.World.Objects <> state.Objects
            || previous.World.BehaviorModules <> behaviorModules
        let characterChanged =
            previous.Character.LocationId <> state.Player.LocationId
            || previous.Character.Inventory <> state.Player.Inventory

        { FormatVersion = CurrentFormatVersion
          World =
            { Revision = previous.World.Revision + (if worldChanged then 1L else 0L)
              ItemIds = state.ItemIds
              BehaviorModules = behaviorModules
              Objects = state.Objects }
          Character =
            { previous.Character with
                Revision = previous.Character.Revision + (if characterChanged then 1L else 0L)
                LocationId = state.Player.LocationId
                Inventory = state.Player.Inventory } }

type InMemoryGameStore(initialState: GameState, ?clock: unit -> DateTimeOffset) =
    let clock = defaultArg clock (fun () -> DateTimeOffset.UtcNow)
    let gate = obj()
    let mutable runtimeState = initialState
    let mutable snapshot = GameSnapshots.create (clock ()) initialState

    member _.Read() =
        lock gate (fun () ->
            { State = runtimeState
              WorldRevision = snapshot.World.Revision
              CharacterRevision = snapshot.Character.Revision })

    member _.GetSnapshot() = lock gate (fun () -> snapshot)

    member _.TryCommit(expectedWorldRevision, expectedCharacterRevision, state: GameState) =
        lock gate (fun () ->
            if expectedWorldRevision <> snapshot.World.Revision
               || expectedCharacterRevision <> snapshot.Character.Revision then
                Error
                    { ExpectedWorldRevision = expectedWorldRevision
                      ActualWorldRevision = snapshot.World.Revision
                      ExpectedCharacterRevision = expectedCharacterRevision
                      ActualCharacterRevision = snapshot.Character.Revision }
            else
                snapshot <- GameSnapshots.update (clock ()) snapshot state
                runtimeState <- state
                Ok
                    { State = runtimeState
                      WorldRevision = snapshot.World.Revision
                      CharacterRevision = snapshot.Character.Revision })

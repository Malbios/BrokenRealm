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

type AccountSnapshot =
    { Id: AccountId
      DisplayName: string option }

type CharacterSnapshot =
    { Id: string
      AccountId: AccountId
      Revision: int64
      LocationId: ObjectId
      Inventory: Map<ItemId, Quantity> }

type GameSnapshot =
    { FormatVersion: int
      World: WorldSnapshot
      Accounts: Map<AccountId, AccountSnapshot>
      Characters: Map<CharacterId, CharacterSnapshot> }

type StoredGameState =
    { State: GameState
      WorldRevision: int64
      CharacterRevisions: Map<CharacterId, int64> }

type CommitConflict =
    { ExpectedWorldRevision: int64
      ActualWorldRevision: int64
      ExpectedCharacterRevisions: Map<CharacterId, int64>
      ActualCharacterRevisions: Map<CharacterId, int64> }

module GameSnapshots =
    [<Literal>]
    let CurrentFormatVersion = 2

    [<Literal>]
    let PrototypeAccountId = "prototype-account"

    [<Literal>]
    let PrototypeCharacterId = "prototype-player"

    [<Literal>]
    let PrototypeScoutCharacterId = "prototype-scout"

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
          Accounts =
            state.Accounts
            |> Map.map (fun _ account -> { Id = account.Id; DisplayName = account.DisplayName })
          Characters =
            state.Characters
            |> Map.map (fun id character ->
                { Id = id
                  AccountId = character.AccountId
                  Revision = 0L
                  LocationId = character.LocationId
                  Inventory = character.Inventory }) }

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
        let accounts =
            state.Accounts
            |> Map.map (fun id account ->
                match previous.Accounts |> Map.tryFind id with
                | Some stored -> { stored with DisplayName = account.DisplayName }
                | None -> { Id = account.Id; DisplayName = account.DisplayName })

        let worldChanged =
            previous.World.ItemIds <> state.ItemIds
            || previous.World.Objects <> state.Objects
            || previous.World.BehaviorModules <> behaviorModules
            || previous.Accounts <> accounts
        let characters =
            state.Characters
            |> Map.map (fun id character ->
                match previous.Characters |> Map.tryFind id with
                | Some stored ->
                    { stored with
                        AccountId = character.AccountId
                        Revision =
                            stored.Revision
                            + (if stored.LocationId <> character.LocationId || stored.Inventory <> character.Inventory then 1L else 0L)
                        LocationId = character.LocationId
                        Inventory = character.Inventory }
                | None ->
                    { Id = id
                      AccountId = character.AccountId
                      Revision = 0L
                      LocationId = character.LocationId
                      Inventory = character.Inventory })

        { FormatVersion = CurrentFormatVersion
          World =
            { Revision = previous.World.Revision + (if worldChanged then 1L else 0L)
              ItemIds = state.ItemIds
              BehaviorModules = behaviorModules
              Objects = state.Objects }
          Accounts = accounts
          Characters = characters }

type InMemoryGameStore(initialState: GameState, ?clock: unit -> DateTimeOffset, ?seedSnapshot: GameSnapshot) =
    let clock = defaultArg clock (fun () -> DateTimeOffset.UtcNow)
    let gate = obj()
    let mutable runtimeState = initialState
    let mutable snapshot =
        match seedSnapshot with
        | Some loaded -> loaded
        | None -> GameSnapshots.create (clock ()) initialState

    member _.Read() =
        lock gate (fun () ->
            { State = runtimeState
              WorldRevision = snapshot.World.Revision
              CharacterRevisions = snapshot.Characters |> Map.map (fun _ character -> character.Revision) })

    member _.GetSnapshot() = lock gate (fun () -> snapshot)

    member _.TryCommit(expectedWorldRevision, expectedCharacterRevisions, state: GameState) =
        lock gate (fun () ->
            let actualCharacterRevisions = snapshot.Characters |> Map.map (fun _ character -> character.Revision)
            if expectedWorldRevision <> snapshot.World.Revision
               || expectedCharacterRevisions <> actualCharacterRevisions then
                Error
                    { ExpectedWorldRevision = expectedWorldRevision
                      ActualWorldRevision = snapshot.World.Revision
                      ExpectedCharacterRevisions = expectedCharacterRevisions
                      ActualCharacterRevisions = actualCharacterRevisions }
            else
                snapshot <- GameSnapshots.update (clock ()) snapshot state
                runtimeState <- state
                Ok
                    { State = runtimeState
                      WorldRevision = snapshot.World.Revision
                      CharacterRevisions = snapshot.Characters |> Map.map (fun _ character -> character.Revision) })

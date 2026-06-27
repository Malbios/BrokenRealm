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
      DisplayName: string option
      PasswordHash: string option }

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
      Characters: Map<CharacterId, CharacterSnapshot>
      PlayerRevisions: Map<CharacterId, int64> }

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
    let CurrentFormatVersion = 3

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

    let private nonPlayerObjects (objects: Map<ObjectId, GameObject>) =
        objects |> Map.filter (fun _ gameObject -> not (PlayerObjects.isPlayer gameObject))

    let private playerIds (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.choose (fun (id, gameObject) -> if PlayerObjects.isPlayer gameObject then Some id else None)

    let private playerRevisionSeed (state: GameState) =
        playerIds state
        |> List.map (fun id -> id, 0L)
        |> Map.ofList

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
            |> Map.map (fun _ account ->
                { Id = account.Id
                  DisplayName = account.DisplayName
                  PasswordHash = account.PasswordHash })
          Characters = Map.empty
          PlayerRevisions = playerRevisionSeed state }

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

    let private playerChanged (previousPlayer: GameObject) (currentPlayer: GameObject) =
        previousPlayer.LocationId <> currentPlayer.LocationId
        || PlayerObjects.inventory previousPlayer <> PlayerObjects.inventory currentPlayer
        || PlayerObjects.accountId previousPlayer <> PlayerObjects.accountId currentPlayer

    let update now previous (state: GameState) =
        let behaviorModules = updateBehaviors now previous.World.BehaviorModules state.BehaviorModules
        let accounts =
            state.Accounts
            |> Map.map (fun id account ->
                match previous.Accounts |> Map.tryFind id with
                | Some stored ->
                    { stored with
                        DisplayName = account.DisplayName
                        PasswordHash = account.PasswordHash }
                | None ->
                    { Id = account.Id
                      DisplayName = account.DisplayName
                      PasswordHash = account.PasswordHash })

        let worldChanged =
            previous.World.ItemIds <> state.ItemIds
            || nonPlayerObjects previous.World.Objects <> nonPlayerObjects state.Objects
            || previous.World.BehaviorModules <> behaviorModules
            || previous.Accounts <> accounts

        let playerRevisions =
            playerIds state
            |> List.map (fun playerId ->
                let currentPlayer = state.Objects[playerId]

                match previous.World.Objects |> Map.tryFind playerId with
                | Some previousPlayer ->
                    let revision =
                        match previous.PlayerRevisions |> Map.tryFind playerId with
                        | Some storedRevision ->
                            storedRevision + (if playerChanged previousPlayer currentPlayer then 1L else 0L)
                        | None -> 0L

                    playerId, revision
                | None -> playerId, 0L)
            |> Map.ofList

        { FormatVersion = CurrentFormatVersion
          World =
            { Revision = previous.World.Revision + (if worldChanged then 1L else 0L)
              ItemIds = state.ItemIds
              BehaviorModules = behaviorModules
              Objects = state.Objects }
          Accounts = accounts
          Characters = Map.empty
          PlayerRevisions = playerRevisions }

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
              CharacterRevisions = snapshot.PlayerRevisions })

    member _.GetSnapshot() = lock gate (fun () -> snapshot)

    member _.TryCommit(expectedWorldRevision, expectedCharacterRevisions, state: GameState) =
        lock gate (fun () ->
            let actualCharacterRevisions = snapshot.PlayerRevisions
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
                      CharacterRevisions = snapshot.PlayerRevisions })
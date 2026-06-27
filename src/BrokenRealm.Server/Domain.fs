namespace BrokenRealm.Server

open System
open System.Text.RegularExpressions

type Culture =
    | En
    | De

type ObjectId = string
type ItemId = string
type Quantity = int
type CharacterId = string
type AccountId = string
type SessionId = string

type ValuePathSegment =
    | PropertySegment of string
    | IndexSegment of int

type GameValue =
    | NullValue
    | StringValue of string
    | IntegerValue of int64
    | FloatValue of double
    | BooleanValue of bool
    | ObjectReferenceValue of ObjectId
    | ListValue of GameValue list
    | MapValue of Map<string, GameValue>
    | AnonymousValue of AnonymousBehaviorValue

and AnonymousBehaviorValue =
    { BehaviorModuleId: string
      BehaviorClassName: string
      Properties: Map<string, GameValue> }

module ObjectIds =
    let private pattern = Regex("^[a-z][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)

    let isValid (value: string) =
        not (String.IsNullOrWhiteSpace value) && pattern.IsMatch value

    let tryParse value =
        if isValid value then
            Ok value
        else
            Error "Object IDs must be 1-64 lowercase ASCII characters, start with a letter, and contain only letters, digits, underscores, or hyphens."

    let create () : ObjectId =
        "obj_" + Guid.CreateVersion7().ToString("N")

type VerbPattern =
    { Culture: Culture
      Pattern: string }

type BehaviorCommand =
    { MethodName: string
      Patterns: VerbPattern list }

type BehaviorClassDefinition =
    { ClassName: string
      Commands: BehaviorCommand list }

type BehaviorModule =
    { Id: string
      RegistryName: string
      Dependencies: string list
      Source: string
      CompiledSource: string
      Classes: Map<string, BehaviorClassDefinition> }

type GameObject =
    { Id: ObjectId
      Name: string
      NameKey: string
      Aliases: Map<Culture, string list>
      DescriptionKey: string option
      LocationId: ObjectId option
      Tags: Set<string>
      Properties: Map<string, GameValue>
      References: Map<string, ObjectId>
      BehaviorModuleId: string
      BehaviorClassName: string }

type AccountState =
    { Id: AccountId
      DisplayName: string option
      PasswordHash: string option }

type GameState =
    { ItemIds: Set<ItemId>
      BehaviorModules: Map<string, BehaviorModule>
      Objects: Map<ObjectId, GameObject>
      Accounts: Map<AccountId, AccountState> }

type GameSession =
    { Id: SessionId
      AccountId: AccountId
      SelectedCharacterId: CharacterId
      Authenticated: bool
      CreatedAt: DateTimeOffset
      LastSeenAt: DateTimeOffset }

type Message =
    { Key: string
      Args: Map<string, string> }

type CommandResult =
    { State: GameState
      Messages: Message list }

type BehaviorUpdateResult =
    { State: GameState
      AffectedModules: string list
      AffectedObjects: ObjectId list }

type BehaviorValidationResult =
    { AffectedModules: string list
      AffectedObjects: ObjectId list }

type MatchedBehaviorMethod =
    { ObjectId: ObjectId
      BehaviorModuleId: string
      BehaviorClassName: string
      MethodName: string
      CompiledSource: string
      Args: Map<string, string> }

type ScriptEffect =
    | AddInventory of objectId: ObjectId option * itemId: ItemId * amount: int
    | MoveObject of objectId: ObjectId option * destinationId: ObjectId
    | TransferItem of objectId: ObjectId option * itemId: ItemId * amount: int * destinationId: ObjectId
    | ReplaceValue of path: ValuePathSegment list * value: GameValue
    | InvokeAnonymous of path: ValuePathSegment list * methodName: string * args: Map<string, string>
    | EmitMessage of Message

[<CLIMutable>]
type GameCommandRequest =
    { text: string
      culture: string }

[<CLIMutable>]
type CommandResponse =
    { lines: string list }

[<CLIMutable>]
type SessionCharacterResponse =
    { id: string
      locationId: string
      displayName: string }

[<CLIMutable>]
type GameSessionResponse =
    { accountId: string
      authenticated: bool
      displayName: string option
      selectedCharacterId: string
      characters: SessionCharacterResponse list }

[<CLIMutable>]
type LoginRequest =
    { accountId: string
      password: string }

[<CLIMutable>]
type RegisterRequest =
    { accountId: string
      password: string
      displayName: string option }

[<CLIMutable>]
type AuthResponse =
    { accountId: string
      authenticated: bool
      displayName: string option
      selectedCharacterId: string
      characters: SessionCharacterResponse list }

type SnapshotBackupInfo =
    { fileName: string
      createdAt: DateTimeOffset }

[<CLIMutable>]
type SnapshotBackupResponse =
    { fileName: string
      formatVersion: int
      worldRevision: int64 }

[<CLIMutable>]
type SnapshotBackupListResponse =
    { backups: SnapshotBackupInfo list }

[<CLIMutable>]
type SnapshotRestoreRequest =
    { fileName: string }

[<CLIMutable>]
type SnapshotRestoreResponse =
    { fileName: string
      formatVersion: int
      worldRevision: int64
      objectCount: int }

[<CLIMutable>]
type SelectCharacterRequest =
    { characterId: string }

[<CLIMutable>]
type SelectCharacterResponse =
    { accountId: string
      authenticated: bool
      displayName: string option
      selectedCharacterId: string
      characters: SessionCharacterResponse list }

[<CLIMutable>]
type BehaviorModuleResponse =
    { moduleId: string
      sourceRevision: int64
      dependencies: string list
      classes: string list
      affectedModules: string list
      affectedObjects: string list
      source: string }

[<CLIMutable>]
type AdminBehaviorModuleResponse =
    { moduleId: string
      dependencies: string list
      classes: string list }

[<CLIMutable>]
type BehaviorModuleUpdateRequest =
    { source: string
      expectedSourceRevision: int64 }

[<CLIMutable>]
type BehaviorModuleUpdateResponse =
    { moduleId: string
      sourceRevision: int64
      source: string
      affectedModules: string list
      affectedObjects: string list
      diagnostics: string list }

[<CLIMutable>]
type CompilerDiagnostic =
    { message: string
      file: string
      line: int
      column: int }

[<CLIMutable>]
type BehaviorModuleErrorResponse =
    { diagnostics: CompilerDiagnostic list }

[<CLIMutable>]
type BehaviorModuleConflictResponse =
    { moduleId: string
      expectedSourceRevision: int64
      currentSourceRevision: int64
      message: string }

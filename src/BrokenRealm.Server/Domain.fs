namespace BrokenRealm.Server

open System
open System.Text.RegularExpressions

type Culture =
    | En
    | De

type ObjectId = string
type ItemId = string
type Quantity = int

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

type PlayerState =
    { LocationId: ObjectId
      Inventory: Map<ItemId, Quantity> }

type GameState =
    { Player: PlayerState
      ItemIds: Set<ItemId>
      BehaviorModules: Map<string, BehaviorModule>
      Objects: Map<ObjectId, GameObject> }

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

type MatchedBehaviorMethod =
    { ObjectId: ObjectId
      BehaviorModuleId: string
      BehaviorClassName: string
      MethodName: string
      CompiledSource: string
      Args: Map<string, string> }

type ScriptEffect =
    | AddInventory of itemId: ItemId * amount: int
    | MovePlayer of destinationId: ObjectId
    | EmitMessage of Message

[<CLIMutable>]
type GameCommandRequest =
    { text: string
      culture: string }

[<CLIMutable>]
type CommandResponse =
    { lines: string list }

[<CLIMutable>]
type BehaviorModuleResponse =
    { moduleId: string
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
    { source: string }

[<CLIMutable>]
type BehaviorModuleUpdateResponse =
    { moduleId: string
      source: string
      affectedModules: string list
      affectedObjects: string list
      diagnostics: string list }

[<CLIMutable>]
type CompilerDiagnostic =
    { message: string
      line: int
      column: int }

[<CLIMutable>]
type BehaviorModuleErrorResponse =
    { diagnostics: CompilerDiagnostic list }

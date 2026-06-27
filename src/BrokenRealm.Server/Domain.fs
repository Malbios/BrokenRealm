namespace BrokenRealm.Server

type Culture =
    | En
    | De

type ObjectId = string
type VerbName = string
type ItemId = string
type Quantity = int

type VerbPattern =
    { Culture: Culture
      Pattern: string }

type Verb =
    { Name: VerbName
      Patterns: VerbPattern list
      Source: string
      CompiledSource: string }

type GameObject =
    { Id: ObjectId
      Name: string
      DescriptionKey: string option
      Tags: Set<string>
      Properties: Map<string, string>
      References: Map<string, ObjectId>
      Verbs: Map<VerbName, Verb> }

type PlayerState =
    { LocationId: ObjectId
      Inventory: Map<ItemId, Quantity> }

type GameState =
    { Player: PlayerState
      ItemIds: Set<ItemId>
      Objects: Map<ObjectId, GameObject> }

type Message =
    { Key: string
      Args: Map<string, string> }

type CommandResult =
    { State: GameState
      Messages: Message list }

type MatchedVerb =
    { ObjectId: ObjectId
      Verb: Verb
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
type VerbResponse =
    { objectId: string
      verb: string
      source: string }

[<CLIMutable>]
type AdminObjectResponse =
    { objectId: string
      name: string
      verbs: string list }

[<CLIMutable>]
type VerbUpdateRequest =
    { source: string }

[<CLIMutable>]
type VerbUpdateResponse =
    { objectId: string
      verb: string
      source: string
      diagnostics: string list }

[<CLIMutable>]
type VerbErrorResponse =
    { diagnostics: string list }

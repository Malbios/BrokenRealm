namespace BrokenRealm.Server

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes

[<AutoOpen>]
module private SnapshotResultBuilder =
    type ResultBuilder() =
        member _.Bind(result, binder) = Result.bind binder result
        member _.Return value = Ok value
        member _.ReturnFrom result = result
        member _.Zero() = Ok ()

    let result = ResultBuilder()

module SnapshotCodec =
    let private jsonOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private (|JsonString|_|) (node: JsonNode) =
        match node with
        | null -> None
        | _ when node.GetValueKind() = JsonValueKind.String -> Some(node.GetValue<string>())
        | _ -> None

    let private (|JsonNumber|_|) (node: JsonNode) =
        match node with
        | null -> None
        | _ when node.GetValueKind() = JsonValueKind.Number -> Some(node.GetValue<decimal>())
        | _ -> None

    let private (|JsonBool|_|) (node: JsonNode) =
        match node with
        | null -> None
        | _ when node.GetValueKind() = JsonValueKind.True -> Some true
        | _ when node.GetValueKind() = JsonValueKind.False -> Some false
        | _ -> None

    let private (|JsonArray|_|) (node: JsonNode) =
        match node with
        | null -> None
        | _ when node.GetValueKind() = JsonValueKind.Array -> Some(node.AsArray())
        | _ -> None

    let private (|JsonObject|_|) (node: JsonNode) =
        match node with
        | null -> None
        | _ when node.GetValueKind() = JsonValueKind.Object -> Some(node.AsObject())
        | _ -> None

    let private requireProperty (name: string) (object: JsonObject) =
        match object[name] with
        | null -> Error $"Snapshot property '{name}' is required."
        | value -> Ok value

    let private decodeCulture value =
        match value with
        | "en" -> Ok En
        | "de" -> Ok De
        | _ -> Error $"Unsupported culture value: {value}"

    let rec private encodeGameValue value =
        match value with
        | NullValue ->
            let object = JsonObject()
            object["kind"] <- JsonValue.Create("null")
            object
        | StringValue text ->
            let object = JsonObject()
            object["kind"] <- JsonValue.Create("string")
            object["value"] <- JsonValue.Create(text)
            object
        | IntegerValue number ->
            let object = JsonObject()
            object["kind"] <- JsonValue.Create("integer")
            object["value"] <- JsonValue.Create(number)
            object
        | FloatValue number ->
            let object = JsonObject()
            object["kind"] <- JsonValue.Create("float")
            object["value"] <- JsonValue.Create(number)
            object
        | BooleanValue flag ->
            let object = JsonObject()
            object["kind"] <- JsonValue.Create("boolean")
            object["value"] <- JsonValue.Create(flag)
            object
        | ObjectReferenceValue objectId ->
            let object = JsonObject()
            object["kind"] <- JsonValue.Create("objectReference")
            object["value"] <- JsonValue.Create(objectId)
            object
        | ListValue values ->
            let object = JsonObject()
            let items = JsonArray()

            values
            |> List.iter (fun item -> items.Add(encodeGameValue item))

            object["kind"] <- JsonValue.Create("list")
            object["items"] <- items
            object
        | MapValue values ->
            let object = JsonObject()
            let entries = JsonObject()

            values
            |> Map.iter (fun (key: string) value -> entries[key] <- encodeGameValue value)

            object["kind"] <- JsonValue.Create("map")
            object["entries"] <- entries
            object
        | AnonymousValue anonymous ->
            let object = JsonObject()
            let properties = JsonObject()

            anonymous.Properties
            |> Map.iter (fun (key: string) value -> properties[key] <- encodeGameValue value)

            object["kind"] <- JsonValue.Create("anonymous")
            object["behaviorModuleId"] <- JsonValue.Create(anonymous.BehaviorModuleId)
            object["behaviorClassName"] <- JsonValue.Create(anonymous.BehaviorClassName)
            object["properties"] <- properties
            object

    and private decodeGameValue node =
        match node with
        | JsonObject object ->
            match object["kind"] with
            | JsonString "null" -> Ok NullValue
            | JsonString "string" ->
                match object["value"] with
                | JsonString value -> Ok(StringValue value)
                | _ -> Error "String game values require a string 'value' property."
            | JsonString "integer" ->
                match object["value"] with
                | JsonNumber number -> Ok(IntegerValue(int64 number))
                | _ -> Error "Integer game values require a numeric 'value' property."
            | JsonString "float" ->
                match object["value"] with
                | JsonNumber number -> Ok(FloatValue(double number))
                | _ -> Error "Float game values require a numeric 'value' property."
            | JsonString "boolean" ->
                match object["value"] with
                | JsonBool flag -> Ok(BooleanValue flag)
                | _ -> Error "Boolean game values require a boolean 'value' property."
            | JsonString "objectReference" ->
                match object["value"] with
                | JsonString objectId -> Ok(ObjectReferenceValue objectId)
                | _ -> Error "Object reference game values require a string 'value' property."
            | JsonString "list" ->
                match object["items"] with
                | JsonArray items ->
                    items
                    |> Seq.map decodeGameValue
                    |> Seq.fold
                        (fun result item ->
                            result
                            |> Result.bind (fun values ->
                                match item with
                                | Ok value -> Ok(value :: values)
                                | Error error -> Error error))
                        (Ok [])
                    |> Result.map List.rev
                    |> Result.map ListValue
                | _ -> Error "List game values require an 'items' array."
            | JsonString "map" ->
                match object["entries"] with
                | JsonObject entries ->
                    entries
                    |> Seq.fold
                        (fun result (KeyValue(key, value)) ->
                            result
                            |> Result.bind (fun values ->
                                decodeGameValue value
                                |> Result.map (fun decoded -> Map.add key decoded values)))
                        (Ok Map.empty)
                    |> Result.map MapValue
                | _ -> Error "Map game values require an 'entries' object."
            | JsonString "anonymous" ->
                match object["behaviorModuleId"], object["behaviorClassName"], object["properties"] with
                | JsonString moduleId, JsonString className, JsonObject properties ->
                    properties
                    |> Seq.fold
                        (fun result (KeyValue(key, value)) ->
                            result
                            |> Result.bind (fun decodedProperties ->
                                decodeGameValue value
                                |> Result.map (fun decoded -> Map.add key decoded decodedProperties)))
                        (Ok Map.empty)
                    |> Result.map (fun decodedProperties ->
                        AnonymousValue
                            { BehaviorModuleId = moduleId
                              BehaviorClassName = className
                              Properties = decodedProperties })
                | _ -> Error "Anonymous game values require behaviorModuleId, behaviorClassName, and properties."
            | JsonString kind -> Error $"Unsupported game value kind: {kind}"
            | _ -> Error "Game values require a string 'kind' property."
        | _ -> Error "Game values must be JSON objects."

    let private encodeStringMap values =
        let object = JsonObject()
        values |> Map.iter (fun (key: string) (value: string) -> object[key] <- JsonValue.Create(value))
        object

    let private decodeStringMap node =
        match node with
        | JsonObject object ->
            object
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun values ->
                        match value with
                        | JsonString text -> Ok(Map.add key text values)
                        | _ -> Error $"Map entry '{key}' must be a string."))
                (Ok Map.empty)
        | _ -> Error "Expected a JSON object."

    let private encodeCultureMap values =
        let object = JsonObject()

        values
        |> Map.iter (fun culture aliases ->
            let key =
                match culture with
                | En -> "en"
                | De -> "de"

            let encoded = JsonArray()

            aliases
            |> List.iter (fun (alias: string) -> encoded.Add(JsonValue.Create(alias)))

            object[key] <- encoded)

        object

    let private decodeCultureMap node =
        match node with
        | JsonObject object ->
            object
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun values ->
                        decodeCulture key
                        |> Result.bind (fun culture ->
                            match value with
                            | JsonArray aliases ->
                                aliases
                                |> Seq.fold
                                    (fun aliasResult alias ->
                                        aliasResult
                                        |> Result.bind (fun collected ->
                                            match alias with
                                            | JsonString text -> Ok(text :: collected)
                                            | _ -> Error $"Alias list for culture '{key}' must contain strings."))
                                    (Ok [])
                                |> Result.map List.rev
                                |> Result.map (fun aliases -> Map.add culture aliases values)
                            | _ -> Error $"Aliases for culture '{key}' must be an array.")))
                (Ok Map.empty)
        | _ -> Error "Expected a JSON object."

    let private encodeProperties values =
        let object = JsonObject()
        values |> Map.iter (fun (key: string) value -> object[key] <- encodeGameValue value)
        object

    let private decodeProperties node =
        match node with
        | JsonObject object ->
            object
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun values ->
                        decodeGameValue value
                        |> Result.map (fun decoded -> Map.add key decoded values)))
                (Ok Map.empty)
        | _ -> Error "Object properties must be a JSON object."

    let private encodeGameObject (gameObject: GameObject) =
        let object = JsonObject()
        object["id"] <- JsonValue.Create(gameObject.Id)
        object["name"] <- JsonValue.Create(gameObject.Name)
        object["nameKey"] <- JsonValue.Create(gameObject.NameKey)
        object["aliases"] <- encodeCultureMap gameObject.Aliases

        let tags = JsonArray()

        gameObject.Tags
        |> Seq.sort
        |> Seq.iter (fun tag -> tags.Add(JsonValue.Create(tag)))

        object["tags"] <- tags
        object["properties"] <- encodeProperties gameObject.Properties
        object["references"] <- encodeStringMap gameObject.References
        object["behaviorModuleId"] <- JsonValue.Create(gameObject.BehaviorModuleId)
        object["behaviorClassName"] <- JsonValue.Create(gameObject.BehaviorClassName)

        match gameObject.DescriptionKey with
        | Some descriptionKey -> object["descriptionKey"] <- JsonValue.Create(descriptionKey)
        | None -> ()

        match gameObject.LocationId with
        | Some locationId -> object["locationId"] <- JsonValue.Create(locationId)
        | None -> ()

        object

    let private decodeGameObject node =
        match node with
        | JsonObject object ->
            result {
                let! id =
                    requireProperty "id" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Object id must be a string.")

                let! name =
                    requireProperty "name" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Object name must be a string.")

                let! nameKey =
                    requireProperty "nameKey" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Object nameKey must be a string.")

                let! aliases =
                    requireProperty "aliases" object
                    |> Result.bind decodeCultureMap

                let! descriptionKey =
                    match object["descriptionKey"] with
                    | JsonString value -> Ok(Some value)
                    | null -> Ok None
                    | _ -> Error "Object descriptionKey must be a string when present."

                let! locationId =
                    match object["locationId"] with
                    | JsonString value -> Ok(Some value)
                    | null -> Ok None
                    | _ -> Error "Object locationId must be a string when present."

                let! tags =
                    requireProperty "tags" object
                    |> Result.bind (function
                        | JsonArray values ->
                            values
                            |> Seq.fold
                                (fun result value ->
                                    result
                                    |> Result.bind (fun collected ->
                                        match value with
                                        | JsonString tag -> Ok(tag :: collected)
                                        | _ -> Error "Object tags must be strings."))
                                (Ok [])
                            |> Result.map (List.rev >> Set.ofList)
                        | _ -> Error "Object tags must be an array.")

                let! properties =
                    requireProperty "properties" object
                    |> Result.bind decodeProperties

                let! references =
                    requireProperty "references" object
                    |> Result.bind decodeStringMap

                let! behaviorModuleId =
                    requireProperty "behaviorModuleId" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Object behaviorModuleId must be a string.")

                let! behaviorClassName =
                    requireProperty "behaviorClassName" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Object behaviorClassName must be a string.")

                return
                    { Id = id
                      Name = name
                      NameKey = nameKey
                      Aliases = aliases
                      DescriptionKey = descriptionKey
                      LocationId = locationId
                      Tags = tags
                      Properties = properties
                      References = references
                      BehaviorModuleId = behaviorModuleId
                      BehaviorClassName = behaviorClassName }
            }
        | _ -> Error "Game objects must be JSON objects."

    let private encodeBehaviorModule (behaviorModule: BehaviorModuleSnapshot) =
        let object = JsonObject()
        let dependencies = JsonArray()

        behaviorModule.Dependencies
        |> List.iter (fun dependency -> dependencies.Add(JsonValue.Create(dependency)))

        object["id"] <- JsonValue.Create(behaviorModule.Id)
        object["registryName"] <- JsonValue.Create(behaviorModule.RegistryName)
        object["dependencies"] <- dependencies
        object["source"] <- JsonValue.Create(behaviorModule.Source)
        object["sourceRevision"] <- JsonValue.Create(behaviorModule.SourceRevision)
        object["activationRevision"] <- JsonValue.Create(behaviorModule.ActivationRevision)
        object["activatedAt"] <- JsonValue.Create(behaviorModule.ActivatedAt.ToString("o"))
        object

    let private decodeBehaviorModule node =
        match node with
        | JsonObject object ->
            result {
                let! id =
                    requireProperty "id" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Behavior module id must be a string.")

                let! registryName =
                    requireProperty "registryName" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Behavior module registryName must be a string.")

                let! dependencies =
                    requireProperty "dependencies" object
                    |> Result.bind (function
                        | JsonArray values ->
                            values
                            |> Seq.fold
                                (fun result value ->
                                    result
                                    |> Result.bind (fun collected ->
                                        match value with
                                        | JsonString dependency -> Ok(dependency :: collected)
                                        | _ -> Error "Behavior module dependencies must be strings."))
                                (Ok [])
                            |> Result.map List.rev
                        | _ -> Error "Behavior module dependencies must be an array.")

                let! source =
                    requireProperty "source" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Behavior module source must be a string.")

                let! sourceRevision =
                    requireProperty "sourceRevision" object
                    |> Result.bind (function
                        | JsonNumber value -> Ok(int64 value)
                        | _ -> Error "Behavior module sourceRevision must be a number.")

                let! activationRevision =
                    requireProperty "activationRevision" object
                    |> Result.bind (function
                        | JsonNumber value -> Ok(int64 value)
                        | _ -> Error "Behavior module activationRevision must be a number.")

                let! activatedAt =
                    requireProperty "activatedAt" object
                    |> Result.bind (function
                        | JsonString value ->
                            match DateTimeOffset.TryParse(value) with
                            | true, parsed -> Ok parsed
                            | false, _ -> Error "Behavior module activatedAt must be an ISO-8601 timestamp."
                        | _ -> Error "Behavior module activatedAt must be a string.")

                return
                    { Id = id
                      RegistryName = registryName
                      Dependencies = dependencies
                      Source = source
                      SourceRevision = sourceRevision
                      ActivationRevision = activationRevision
                      ActivatedAt = activatedAt }
            }
        | _ -> Error "Behavior modules must be JSON objects."

    let private encodeAccount (account: AccountSnapshot) =
        let object = JsonObject()
        object["id"] <- JsonValue.Create(account.Id)

        match account.DisplayName with
        | Some displayName -> object["displayName"] <- JsonValue.Create(displayName)
        | None -> ()

        object

    let private decodeAccount node =
        match node with
        | JsonObject object ->
            result {
                let! id =
                    requireProperty "id" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Account id must be a string.")

                let! displayName =
                    match object["displayName"] with
                    | JsonString value -> Ok(Some value)
                    | null -> Ok None
                    | _ -> Error "Account displayName must be a string when present."

                return { Id = id; DisplayName = displayName }
            }
        | _ -> Error "Accounts must be JSON objects."

    let private encodeAccountMap values =
        let object = JsonObject()
        values |> Map.iter (fun (key: string) value -> object[key] <- encodeAccount value)
        object

    let private decodeAccountMap node =
        match node with
        | JsonObject object ->
            object
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun values ->
                        decodeAccount value
                        |> Result.bind (fun decoded ->
                            if decoded.Id <> key then
                                Error $"Account map key '{key}' does not match embedded account id '{decoded.Id}'."
                            else
                                Ok(Map.add key decoded values))))
                (Ok Map.empty)
        | _ -> Error "Expected a JSON object."

    let private encodeCharacter (character: CharacterSnapshot) =
        let object = JsonObject()
        let inventory = JsonObject()

        character.Inventory
        |> Map.iter (fun itemId quantity -> inventory[itemId] <- JsonValue.Create(quantity))

        object["id"] <- JsonValue.Create(character.Id)
        object["accountId"] <- JsonValue.Create(character.AccountId)
        object["revision"] <- JsonValue.Create(character.Revision)
        object["locationId"] <- JsonValue.Create(character.LocationId)
        object["inventory"] <- inventory
        object

    let private decodeCharacter requireAccountId node =
        match node with
        | JsonObject object ->
            result {
                let! id =
                    requireProperty "id" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Character id must be a string.")

                let! revision =
                    requireProperty "revision" object
                    |> Result.bind (function
                        | JsonNumber value -> Ok(int64 value)
                        | _ -> Error "Character revision must be a number.")

                let! locationId =
                    requireProperty "locationId" object
                    |> Result.bind (function
                        | JsonString value -> Ok value
                        | _ -> Error "Character locationId must be a string.")

                let! inventory =
                    requireProperty "inventory" object
                    |> Result.bind (function
                        | JsonObject entries ->
                            entries
                            |> Seq.fold
                                (fun result (KeyValue(itemId, value)) ->
                                    result
                                    |> Result.bind (fun collected ->
                                        match value with
                                        | JsonNumber quantity -> Ok(Map.add itemId (int quantity) collected)
                                        | _ -> Error $"Inventory entry '{itemId}' must be a number."))
                                (Ok Map.empty)
                        | _ -> Error "Character inventory must be an object.")

                let! accountId =
                    if requireAccountId then
                        requireProperty "accountId" object
                        |> Result.bind (function
                            | JsonString value -> Ok value
                            | _ -> Error "Character accountId must be a string.")
                    else
                        match object["accountId"] with
                        | JsonString value -> Ok value
                        | null -> Ok GameSnapshots.PrototypeAccountId
                        | _ -> Error "Character accountId must be a string when present."

                return
                    { Id = id
                      AccountId = accountId
                      Revision = revision
                      LocationId = locationId
                      Inventory = inventory }
            }
        | _ -> Error "Characters must be JSON objects."

    let private encodeObjectMap values =
        let object = JsonObject()
        values |> Map.iter (fun (key: string) value -> object[key] <- encodeGameObject value)
        object

    let private decodeObjectMap node =
        match node with
        | JsonObject object ->
            object
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun values ->
                        decodeGameObject value
                        |> Result.bind (fun decoded ->
                            if decoded.Id <> key then
                                Error $"Object map key '{key}' does not match embedded object id '{decoded.Id}'."
                            else
                                Ok(Map.add key decoded values))))
                (Ok Map.empty)
        | _ -> Error "Expected a JSON object."

    let private encodeBehaviorModuleMap values =
        let object = JsonObject()
        values |> Map.iter (fun (key: string) value -> object[key] <- encodeBehaviorModule value)
        object

    let private decodeBehaviorModuleMap node =
        match node with
        | JsonObject object ->
            object
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun values ->
                        decodeBehaviorModule value
                        |> Result.bind (fun decoded ->
                            if decoded.Id <> key then
                                Error $"Behavior module map key '{key}' does not match embedded module id '{decoded.Id}'."
                            else
                                Ok(Map.add key decoded values))))
                (Ok Map.empty)
        | _ -> Error "Expected a JSON object."

    let private encodeCharacterMap values =
        let object = JsonObject()
        values |> Map.iter (fun (key: string) value -> object[key] <- encodeCharacter value)
        object

    let private decodeCharacterMap requireAccountId node =
        match node with
        | JsonObject object ->
            object
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun values ->
                        decodeCharacter requireAccountId value
                        |> Result.bind (fun decoded ->
                            if decoded.Id <> key then
                                Error $"Character map key '{key}' does not match embedded character id '{decoded.Id}'."
                            else
                                Ok(Map.add key decoded values))))
                (Ok Map.empty)
        | _ -> Error "Expected a JSON object."

    let private encodeRevisionMap values =
        let object = JsonObject()
        values
        |> Map.iter (fun (key: string) (revision: int64) ->
            object[key] <- JsonValue.Create(revision))
        object

    let private decodeRevisionMap node =
        match node with
        | JsonObject entries ->
            entries
            |> Seq.fold
                (fun result (KeyValue(key, value)) ->
                    result
                    |> Result.bind (fun collected ->
                        match value with
                        | JsonNumber revision -> Ok(Map.add key (int64 revision) collected)
                        | _ -> Error $"Player revision '{key}' must be a number."))
                (Ok Map.empty)
        | _ -> Error "Player revisions must be a JSON object."

    let encode (snapshot: GameSnapshot) =
        let root = JsonObject()
        let world = JsonObject()
        let itemIds = JsonArray()

        snapshot.World.ItemIds
        |> Seq.sort
        |> Seq.iter (fun itemId -> itemIds.Add(JsonValue.Create(itemId)))

        world["revision"] <- JsonValue.Create(snapshot.World.Revision)
        world["itemIds"] <- itemIds
        world["behaviorModules"] <- encodeBehaviorModuleMap snapshot.World.BehaviorModules
        world["objects"] <- encodeObjectMap snapshot.World.Objects
        root["formatVersion"] <- JsonValue.Create(snapshot.FormatVersion)
        root["world"] <- world
        root["accounts"] <- encodeAccountMap snapshot.Accounts

        if snapshot.FormatVersion >= 3 then
            root["playerRevisions"] <- encodeRevisionMap snapshot.PlayerRevisions
        else
            root["characters"] <- encodeCharacterMap snapshot.Characters

        root

    let decode (root: JsonObject) =
        result {
            let! formatVersion =
                requireProperty "formatVersion" root
                |> Result.bind (function
                    | JsonNumber value -> Ok(int value)
                    | _ -> Error "Snapshot formatVersion must be a number.")

            let! worldNode = requireProperty "world" root

            let! world =
                match worldNode with
                | JsonObject world ->
                    result {
                        let! revision =
                            requireProperty "revision" world
                            |> Result.bind (function
                                | JsonNumber value -> Ok(int64 value)
                                | _ -> Error "World revision must be a number.")

                        let! itemIds =
                            requireProperty "itemIds" world
                            |> Result.bind (function
                                | JsonArray values ->
                                    values
                                    |> Seq.fold
                                        (fun result value ->
                                            result
                                            |> Result.bind (fun collected ->
                                                match value with
                                                | JsonString itemId -> Ok(itemId :: collected)
                                                | _ -> Error "World itemIds must be strings."))
                                        (Ok [])
                                    |> Result.map (List.rev >> Set.ofList)
                                | _ -> Error "World itemIds must be an array.")

                        let! behaviorModules =
                            requireProperty "behaviorModules" world
                            |> Result.bind decodeBehaviorModuleMap

                        let! objects =
                            requireProperty "objects" world
                            |> Result.bind decodeObjectMap

                        return
                            { Revision = revision
                              ItemIds = itemIds
                              BehaviorModules = behaviorModules
                              Objects = objects }
                    }
                | _ -> Error "World snapshot must be a JSON object."

            let requireAccountId = formatVersion >= 2

            let! accounts =
                if requireAccountId then
                    requireProperty "accounts" root |> Result.bind decodeAccountMap
                else
                    match root["accounts"] with
                    | JsonObject _ as node -> decodeAccountMap node
                    | null -> Ok Map.empty
                    | _ -> Error "Snapshot accounts must be a JSON object when present."

            let! characters, playerRevisions =
                if formatVersion >= 3 then
                    requireProperty "playerRevisions" root
                    |> Result.bind decodeRevisionMap
                    |> Result.map (fun revisions -> Map.empty, revisions)
                else
                    requireProperty "characters" root
                    |> Result.bind (decodeCharacterMap requireAccountId)
                    |> Result.map (fun characters -> characters, Map.empty)

            return
                { FormatVersion = formatVersion
                  World = world
                  Accounts = accounts
                  Characters = characters
                  PlayerRevisions = playerRevisions }
        }

    let serialize snapshot =
        encode snapshot |> fun node -> node.ToJsonString(jsonOptions)

    let tryDeserialize json =
        try
            match JsonNode.Parse(json: string) with
            | null -> Error "Snapshot JSON is empty."
            | node ->
                match node with
                | JsonObject root -> decode root
                | _ -> Error "Snapshot JSON must be an object."
        with ex ->
            Error $"Snapshot JSON is invalid: {ex.Message}"

    let writeFile (path: string) snapshot =
        let directory = Path.GetDirectoryName(path)

        if not (String.IsNullOrWhiteSpace directory) then
            Directory.CreateDirectory(directory) |> ignore

        let tempPath = path + ".tmp"
        File.WriteAllText(tempPath, serialize snapshot)
        File.Move(tempPath, path, overwrite = true)

    let tryReadFile (path: string) =
        if not (File.Exists path) then
            Error $"Snapshot file does not exist: {path}"
        else
            tryDeserialize (File.ReadAllText(path))
namespace BrokenRealm.Server

module RoomMap =
    [<Literal>]
    let MapCodeProperty = "mapCode"

    [<Literal>]
    let MapRegionProperty = "mapRegion"

    [<Literal>]
    let MapXProperty = "mapX"

    [<Literal>]
    let MapYProperty = "mapY"

    let private fogLabel = "??"

    let private directionDelta direction =
        match direction with
        | "north" -> 0, -1
        | "south" -> 0, 1
        | "east" -> 1, 0
        | "west" -> -1, 0
        | _ -> 0, 0

    let private tryReadMapInteger properties key =
        match properties |> Map.tryFind key with
        | Some(IntegerValue value) -> Some(int value)
        | _ -> None

    let private tryReadMapRegion properties =
        match properties |> Map.tryFind MapRegionProperty with
        | Some(StringValue region) when not (System.String.IsNullOrWhiteSpace region) -> region
        | _ -> "main"

    let private deriveMapCode (nameKey: string) (properties: Map<string, GameValue>) =
        match properties |> Map.tryFind MapCodeProperty with
        | Some(StringValue code) when code.Length = 2 -> code
        | _ ->
            if nameKey.Contains "clearing" then
                "CL"
            elif nameKey.Contains "village" then
                "VI"
            elif nameKey.Contains "forest" then
                "FO"
            else
                "RM"

    let assignMapLayout (sourceRoom: GameObject) direction nameKey (properties: Map<string, GameValue>) =
        let sourceX = tryReadMapInteger sourceRoom.Properties MapXProperty |> Option.defaultValue 0
        let sourceY = tryReadMapInteger sourceRoom.Properties MapYProperty |> Option.defaultValue 0
        let region = tryReadMapRegion sourceRoom.Properties
        let dx, dy = directionDelta direction
        let mapCode = deriveMapCode nameKey properties

        properties
        |> Map.add MapRegionProperty (StringValue region)
        |> Map.add MapXProperty (IntegerValue(int64 (sourceX + dx)))
        |> Map.add MapYProperty (IntegerValue(int64 (sourceY + dy)))
        |> Map.add MapCodeProperty (StringValue mapCode)

    type MapRoom =
        { Id: ObjectId
          MapCode: string
          MapRegion: string
          MapX: int
          MapY: int }

    type MapCellView =
        { X: int
          Y: int
          RoomId: ObjectId
          Label: string
          Visited: bool
          Current: bool }

    type MapView =
        { Region: string
          MinX: int
          MaxX: int
          MinY: int
          MaxY: int
          CurrentRoomId: ObjectId
          Cells: MapCellView list }

    let private tryReadString properties key =
        match properties |> Map.tryFind key with
        | Some(StringValue value) when not (System.String.IsNullOrWhiteSpace value) -> Some value
        | _ -> None

    let private tryReadInteger properties key =
        match properties |> Map.tryFind key with
        | Some(IntegerValue value) -> Some(int value)
        | _ -> None

    let private isRoom (gameObject: GameObject) =
        gameObject.LocationId.IsNone
        && not (PlayerObjects.isPlayer gameObject)
        && not (CarriedItems.isCarriedStack gameObject)

    let tryMapRoom (gameObject: GameObject) =
        if not (isRoom gameObject) then
            None
        else
            match
                tryReadString gameObject.Properties MapCodeProperty,
                tryReadString gameObject.Properties MapRegionProperty,
                tryReadInteger gameObject.Properties MapXProperty,
                tryReadInteger gameObject.Properties MapYProperty
            with
            | Some mapCode, Some mapRegion, Some mapX, Some mapY ->
                Some
                    { Id = gameObject.Id
                      MapCode = mapCode
                      MapRegion = mapRegion
                      MapX = mapX
                      MapY = mapY }
            | _ -> None

    let private mapRooms (state: GameState) =
        state.Objects
        |> Map.toList
        |> List.choose (fun (_, gameObject) -> tryMapRoom gameObject)

    let private visitedSet (player: GameObject) =
        PlayerObjects.visitedRoomIds player |> Set.ofList

    let buildView (state: GameState) (characterId: CharacterId) =
        match PlayerObjects.tryGet state characterId with
        | None -> None
        | Some player ->
            match player.LocationId with
            | None -> None
            | Some currentRoomId ->
                let visited = visitedSet player
                let currentRegion =
                    state.Objects
                    |> Map.tryFind currentRoomId
                    |> Option.bind tryMapRoom
                    |> Option.map (fun room -> room.MapRegion)
                    |> Option.defaultValue "main"

                let regionRooms =
                    mapRooms state
                    |> List.filter (fun room -> room.MapRegion = currentRegion)

                if List.isEmpty regionRooms then
                    None
                else
                    let cells =
                        regionRooms
                        |> List.map (fun room ->
                            let isCurrent = room.Id = currentRoomId
                            let isVisited = visited.Contains room.Id || isCurrent

                            { X = room.MapX
                              Y = room.MapY
                              RoomId = room.Id
                              Label = if isVisited then room.MapCode else fogLabel
                              Visited = isVisited
                              Current = isCurrent })

                    let minX = cells |> List.map _.X |> List.min
                    let maxX = cells |> List.map _.X |> List.max
                    let minY = cells |> List.map _.Y |> List.min
                    let maxY = cells |> List.map _.Y |> List.max

                    Some
                        { Region = currentRegion
                          MinX = minX
                          MaxX = maxX
                          MinY = minY
                          MaxY = maxY
                          CurrentRoomId = currentRoomId
                          Cells = cells }

    let private formatCellLabel (cell: MapCellView) =
        if cell.Current then
            $"[{cell.Label}]"
        else
            cell.Label

    let formatTextMap (state: GameState) (culture: Culture) (characterId: CharacterId) =
        match buildView state characterId with
        | None -> Localizer.text culture { Key = "map.unavailable"; Args = Map.empty }
        | Some view ->
            [ for y in view.MinY .. view.MaxY do
                  [ for x in view.MinX .. view.MaxX do
                        match view.Cells |> List.tryFind (fun cell -> cell.X = x && cell.Y = y) with
                        | Some cell -> formatCellLabel cell
                        | None -> "  " ]
                  |> String.concat " " ]
            |> String.concat System.Environment.NewLine

    let formatDisplayMessage (state: GameState) (culture: Culture) (characterId: CharacterId) =
        let title = Localizer.text culture { Key = "map.title"; Args = Map.empty }
        let grid = formatTextMap state culture characterId
        title + System.Environment.NewLine + grid

    let toResponse (state: GameState) (characterId: CharacterId) =
        match buildView state characterId with
        | None -> None
        | Some view ->
            Some
                { region = view.Region
                  minX = view.MinX
                  maxX = view.MaxX
                  minY = view.MinY
                  maxY = view.MaxY
                  currentRoomId = view.CurrentRoomId
                  cells =
                    view.Cells
                    |> List.map (fun cell ->
                        { x = cell.X
                          y = cell.Y
                          roomId = cell.RoomId
                          label = cell.Label
                          visited = cell.Visited
                          current = cell.Current }) }
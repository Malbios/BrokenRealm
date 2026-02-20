namespace BrokenRealm.Common

[<RequireQualifiedAccess>]
module Async =
    
    let returnM x =
        async { return x }

namespace BrokenRealm.Server

open System
open System.Security.Cryptography
open System.Text

module Auth =
    [<Literal>]
    let private saltSize = 16

    [<Literal>]
    let private hashSize = 32

    [<Literal>]
    let private iterations = 100_000

    let hashPassword (password: string) =
        let salt = RandomNumberGenerator.GetBytes saltSize

        let hash =
            Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                hashSize)

        Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash)

    let verifyPassword (password: string) (stored: string) =
        match stored.Split(':', 2) with
        | [| saltBase64; hashBase64 |] ->
            let salt = Convert.FromBase64String saltBase64
            let expected = Convert.FromBase64String hashBase64

            let actual =
                Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes password,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length)

            CryptographicOperations.FixedTimeEquals(actual, expected)
        | _ -> false

    let validateAccountId (accountId: string) =
        if String.IsNullOrWhiteSpace accountId then
            Error "Account id is required."
        elif accountId.Length > 64 then
            Error "Account id may contain at most 64 characters."
        elif not (ObjectIds.isValid accountId) then
            Error "Account ids must be lowercase ASCII letters, digits, underscores, or hyphens."
        else
            Ok accountId

    let validatePassword (password: string) =
        if String.IsNullOrEmpty password then
            Error "Password is required."
        elif password.Length < 4 then
            Error "Password must be at least 4 characters."
        elif password.Length > 128 then
            Error "Password may contain at most 128 characters."
        else
            Ok password
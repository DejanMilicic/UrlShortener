module UrlShortener.Database

open System
open FSharp.Data.Sql
open FSharp.Data.Sql.Transactions
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open WebSharper
open WebSharper.AspNetCore
open UrlShortener.DataModel
open System.Linq
open Microsoft.FSharp.Collections
open Raven.Client.Documents

type Sql = SqlDataProvider<
            // Connect to SQLite using System.Data.Sqlite.
            Common.DatabaseProviderTypes.SQLITE,
            SQLiteLibrary = Common.SQLiteLibrary.SystemDataSQLite,
            ResolutionPath = const(__SOURCE_DIRECTORY__ + "/../packages/System.Data.SQLite.Core/lib/netstandard2.0/"),
            // Store the database file in db/urlshortener.db.
            ConnectionString = const("Data Source=" + __SOURCE_DIRECTORY__ + "/db/urlshortener.db"),
            // Store the schema as JSON so that the compiler doesn't need the database to exist.
            ContextSchemaPath = const(__SOURCE_DIRECTORY__ + "/db/urlshortener.schema.json"),
            UseOptionTypes = true>

[<CLIMutable>]
type User = {
    Id : string
    FacebookId : string
    FullName : string
}

[<CLIMutable>]
type Redirection = {
    Id : Link.Id
    CreatorId : string
    Url : string
    VisitCount : int64
}

let tryHead (ls:seq<'a>) : option<'a>  = ls |> Seq.tryPick Some

/// ASP.NET Core service that creates a new data context every time it's required.
type Context(config: IConfiguration, logger: ILogger<Context>) =
    do logger.LogInformation("Creating db context")

    let db =
        let connString = config.GetSection("ConnectionStrings").["UrlShortener"]
        Sql.GetDataContext(connString, TransactionOptions.Default)

    /// Apply all migrations.
    member this.Migrate() =
        try
            use ctx = db.CreateConnection()
            let evolve =
                new Evolve.Evolve(ctx, logger.LogInformation,
                    Locations = ["db/migrations"],
                    IsEraseDisabled = true)
            evolve.Migrate()
        with ex ->
            logger.LogCritical("Database migration failed: {0}", ex)



    /// Get the user for this Facebook user id, or create a user if there isn't one.
    member this.GetOrCreateFacebookUser(fbUserId: string, fbUserName: string) : Async<string> = async {
        use session = Persistence.Store.OpenSession()
        
        let existing = session.Query<User>()
                          .Where(fun user -> user.FacebookId = fbUserId) 
                          .Select(fun user -> user.Id)
                          |> seq |> tryHead
        
        match existing with
        | None ->
            let u = {
                Id = null
                FacebookId = fbUserId
                FullName = fbUserName
            }
            session.Store(u)
            session.SaveChanges()
            return u.Id
        | Some id ->
            return id
    }

    /// Get the user's full name.
    member this.GetUserData(userId: string) : Async<User.Data option> = async {
        use session = Persistence.Store.OpenSession()
        
        let name = session.Query<User>()
                      .Where(fun user -> user.Id = userId)
                      .Select(fun user -> user.FullName)
                      |> seq |> tryHead

        return name |> Option.map (fun name ->
            {
                UserId = userId
                FullName = name
            } : User.Data
        )
    }

    /// Create a new link on this user's behalf, pointing to this url.
    /// Returns the slug for this new link.
    member this.CreateLink(userId: string, url: string) : Async<Link.Slug> = async {
        use session = Persistence.Store.OpenSession()
        
        let r = {
          Id = Link.NewLinkId()
          CreatorId = userId
          Url = url
          VisitCount = 0L
        }

        session.Store(r)
        session.SaveChanges()

        return Link.EncodeLinkId r.Id
    }

    /// Get the url pointed to by the given slug, if any,
    /// and increment its visit count.
    member this.TryVisitLink(slug: Link.Slug) : Async<string option> = async {
        use session = Persistence.Store.OpenSession()
        
        match Link.TryDecodeLinkId slug with
        | None -> return None
        | Some linkId ->
            let link =
                session.Query<Redirection>()
                          .Where(fun l -> l.Id = linkId)
                          |> seq |> tryHead

            match link with
            | None -> return None
            | Some link ->
                session.Advanced.Patch<Redirection, int64>(link.Id, (fun x -> x.VisitCount), link.VisitCount + 1L)
                session.SaveChanges()
                return Some link.Url
    }

    /// Get data about all the links created by the given user.
    member this.GetAllUserLinks (userId: string, ctx: Web.Context) : Async<Link.Data[]> = async {
        use session = Persistence.Store.OpenSession()
        
        let links = session.Query<Redirection>()
                      .Where(fun l -> l.CreatorId = userId)
                      |> seq

        return links
            |> Seq.map (fun l ->
                let slug = Link.EncodeLinkId l.Id
                let url = Link.SlugToFullUrl ctx slug
                {
                    Slug = slug
                    LinkUrl = url
                    TargetUrl = l.Url
                    VisitCount = l.VisitCount
                } : Link.Data
            )
            |> Array.ofSeq
    }

    /// Check that this link belongs to this user, and if yes, delete it.
    member this.DeleteLink(userId: string, slug: Link.Slug) : Async<unit> = async {
        use session = Persistence.Store.OpenSession()
        
        match Link.TryDecodeLinkId slug with
        | None -> return ()
        | Some linkId ->
            let link = session.Query<Redirection>()
                        .Where(fun l -> l.Id = linkId && l.CreatorId = userId)
                        |> seq |> tryHead
            match link with
            | None -> return ()
            | Some l ->
                session.Delete(l)
                session.SaveChanges()
    }

type Web.Context with
    /// Get a new database context.
    member this.Db : Context =
        this.HttpContext().RequestServices.GetRequiredService<Context>()

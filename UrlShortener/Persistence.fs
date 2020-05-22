namespace UrlShortener

open Raven.Client.Documents
open Raven.Client.Documents.Indexes
open System.Reflection
open System.Security.Cryptography.X509Certificates

module Persistence =
  let configure (store : IDocumentStore) =
      //store.Conventions.CustomizeJsonSerializer <- (fun s -> s.Converters.Add(IdiomaticDuConverter()))
      store.Initialize () |> ignore
      IndexCreation.CreateIndexes(Assembly.GetExecutingAssembly(), store)

  let Store = 
      let store = new DocumentStore ()
      store.Urls <-  [|"https://a.free.dejanmilicic.ravendb.cloud/"|]
      store.Database <- "UrlShortener"
      store.Certificate <- new X509Certificate2("free.dejanmilicic.client.certificate.pfx");
      configure store
      store


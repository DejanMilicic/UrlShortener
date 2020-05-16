namespace UrlShortener

open Raven.Client.Documents
open Raven.Client.Documents.Indexes
open Raven.Embedded
open System.Reflection

module Persistence =
  let configure (store : IDocumentStore) =
      //store.Conventions.CustomizeJsonSerializer <- (fun s -> s.Converters.Add(IdiomaticDuConverter()))
      store.Initialize () |> ignore
      IndexCreation.CreateIndexes(Assembly.GetExecutingAssembly(), store)

  let Store = 
      let serverOptions = new ServerOptions()
      serverOptions.ServerUrl <- "http://127.0.0.1:8080"
      serverOptions.ServerDirectory <- "C:\RavenDB\RavenDB-5.0.0-nightly-20200312-0631-windows-x64\Server"

      EmbeddedServer.Instance.StartServer(serverOptions);
      let store = EmbeddedServer.Instance.GetDocumentStore("UrlShortener")
      //store.Database <- "UrlShortener"
      //store.Certificate <- new X509Certificate2("free.dejanmilicic.client.certificate.pfx");
      configure store
      store


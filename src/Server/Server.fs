module Server

open System
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Authentication.OpenIdConnect
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Identity.Web
open Giraffe
open Shared

type RequiresAuthMiddleware(next: RequestDelegate) =
    member this.Invoke (ctx: HttpContext) =
        task {
            let isAuthenticated =
                isNotNull ctx.User
                && ctx.User.Identity.IsAuthenticated

            let isRoot =
                ctx.Request.Path.Value.Equals "/"
                || ctx.Request.Path.Value.Equals "/index.html"

            match isAuthenticated, isRoot with
            | true, _ -> do! next.Invoke(ctx)
            | false, true ->
                do! ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme)
            | false, false ->
                ctx.SetStatusCode(401)
                do! ctx.Response.WriteAsJsonAsync {| Message = "Unauthenticated" |}
        }

module Storage =
    let todos = ResizeArray()

    let addTodo todo =
        if Todo.isValid todo.Description then
            todos.Add todo
            Ok()
        else
            Error "Invalid todo"

    do
        addTodo (Todo.create "Create new SAFE project") |> ignore
        addTodo (Todo.create "Write your app") |> ignore
        addTodo (Todo.create "Ship it!!!") |> ignore

let todosApi = {
    getTodos = fun () -> async { return Storage.todos |> List.ofSeq }
    addTodo =
        fun todo -> async {
            return
                match Storage.addTodo todo with
                | Ok() -> todo
                | Error e -> failwith e
        }
}

let remotingApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromValue todosApi
    |> Remoting.buildHttpHandler

let webApp =
    choose [
        routeStartsWith "/api" >=> remotingApi
    ]

let registerMiddleware (appBuilder : IApplicationBuilder) =
    appBuilder
        .UseAuthentication()
        .UseAuthorization()
        .UseMiddleware<RequiresAuthMiddleware>()
        .UseDefaultFiles()
        .UseStaticFiles()
        .UseGiraffe(webApp)

let registerServices (services : IServiceCollection) =
    let sp = services.BuildServiceProvider()
    let config = sp.GetService<IConfiguration>()
    services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(config)
        .Services
        .AddAuthorization()
        .AddDistributedMemoryCache()
        .AddSession()
    |> ignore

// let app = application {
//     app_config registerMiddleware
//     service_config registerServices
//     use_router webApp
// }

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(
        WebApplicationOptions(
            Args = args,
            WebRootPath = "public"))
    // let builder = WebApplication.CreateBuilder()
    registerServices builder.Services

    let app = builder.Build()
    registerMiddleware app

    app.Run()
    0
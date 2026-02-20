namespace BrokenRealm.Client.Pages

open Bolero.Html
open BrokenRealm.Client
open Microsoft.AspNetCore.Components.Web
open Radzen
open Radzen.Blazor

[<RequireQualifiedAccess>]
module Main =
    
    let render (model: Model) dispatch =
        
        comp<RadzenStack> {
            "Orientation" => Orientation.Vertical
            "JustifyContent" => JustifyContent.Center
            "AlignItems" => AlignItems.Center
            
            comp<RadzenStack> {
                "Orientation" => Orientation.Horizontal
                "JustifyContent" => JustifyContent.Center
                "AlignItems" => AlignItems.Center
                
                comp<RadzenButton> {
                    attr.callback "Click" (fun (_: MouseEventArgs) -> Message.Store "a" |> dispatch) 
                    "a"
                }
                
                comp<RadzenButton> {
                    attr.callback "Click" (fun (_: MouseEventArgs) -> Message.Store "b" |> dispatch) 
                    "b"
                }
            }
            
            comp<RadzenButton> {
                attr.callback "Click" (fun (_: MouseEventArgs) -> Message.ClearStore |> dispatch) 
                "clear"
            }
            
            div { model.TestStorage }
        }

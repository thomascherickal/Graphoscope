﻿namespace Graphoscope.Algorithms

open Graphoscope
open System.Collections.Generic

///Louvain method for community detection
//Blondel, Vincent D; Guillaume, Jean-Loup; Lambiotte, Renaud; Lefebvre, Etienne (9 October 2008). "Fast unfolding of communities in large networks". Journal of Statistical Mechanics: Theory and Experiment. 2008
type Louvain() =                   
   
    /// <summary> 
    /// Takes a FGraph and returns a new graph whose NodeData has been transformed into tupels, where the second part is the community according to modularity-optimization by the Louvain Algorithm (Blondel, Vincent D; Guillaume, Jean-Loup; Lambiotte, Renaud; Lefebvre, Etienne (9 October 2008). "Fast unfolding of communities in large networks". Journal of Statistical Mechanics: Theory and Experiment. 2008 ).
    /// </summary>
    /// <param name="randomized">Boolean, if true randomizes the order in which the vertices are checked. Else the calculations are ordered by the index of the vertices.</param> 
    /// <param name="modularityIncreaseThreshold">Threshold of modularity-difference that has to be exceeded in order to be recognized as a modularity-increase. The value has to be between 0. and 1. to get a meaningful result. The smaller the value, the longer the calculation takes.</param> 
    /// <param name="resolution">The higher the resolution, the smaller the number of communities. The value has to be 1. or higher. Based on : "R. Lambiotte, J.-C. Delvenne, M. Barahona Laplacian Dynamics and Multiscale Modular Structure in Networks 2009", 	arXiv:0812.1770 [physics.soc-ph].</param> 
    /// <param name="graph">FGraph, that is used as the template for the modularity optimization.</param> 
    /// <returns>A new FGraph whose NodeData has been transformed into tupels, where the second part is the community accorging to modularity-optimization.</returns>
    static member louvainResolution (randomized: bool) (modularityIncreaseThreshold: float) (resolution: float) (graph:FGraph<'Node,'Label,'Edge>) : (FGraph<'Node,'Label*'Community,'Edge>) =

        //Return the value to the key k if it is bound, else fail.        
        let getValue k (dict:Dictionary<'K,'V>) =
            try 
                dict.Item k
            with
            | _ -> failwithf "Error get k %O dict %O" k dict

        //All functions connected to the randomization progress.
        //Swaps the position of item x and item y in the array a.
        let swap (a: _[]) x y =
            
            let tmp = a.[x]
            a.[x] <- a.[y]
            a.[y] <- tmp

        // shuffle an array (in-place)
        let shuffle a =
            let rand = new System.Random()
            Seq.iteri (fun i _ -> swap a i (rand.Next(i, Seq.length a))) a  


         //Group values of an array by the groupingF and sum the values of each group after applying the valueF on each of them.
        let inline sumGroupBy (groupingF : 'T -> 'group) (valueF : 'T -> 'V) (input : ('T) []) =
        
            let length = input.Length
            let dict = System.Collections.Generic.Dictionary<'group,'V> ()
    
            // Build the groupings
            for i = 0 to length - 1 do

                let item = input.[i]
                let safeKey,v = groupingF item, valueF item
                let mutable prev = Unchecked.defaultof<'V>
                if dict.TryGetValue(safeKey, &prev) then
                    dict.[safeKey] <- prev + v
                else 
                    //dict.Add(safeKey,v)
                    dict.[safeKey] <- v
             
            // Return the array-of-sums.
            let result = Array.zeroCreate dict.Count
            let mutable i = 0
            for group in dict do
                result.[i] <- group.Key, group.Value
                i <- i + 1
            result

        //Find the summed up weights to the original community of the vertex
        let findWeightofConnectionToOldCommunity connectedCommunities originalCommunity     =   
        
            match (Array.tryFind (fun (community,weight) -> community=originalCommunity) connectedCommunities) with
                | Some x    -> (x|> snd)
                | None      -> 0.


        let louvainMethod (g:FGraph<'Node,'Label,'Edge>) (randomized:bool) (modularityIncreaseThreshold: float) (resolution: float) : (FGraph<'Node,'Label*int,'Edge>) = 

            let nodeToCommunity =
                g.Keys
                |>Seq.mapi(fun i x -> x,i)
                |>Map.ofSeq

            let graphSeq:seq<'Node*'Label*'Node*'Label*'Edge>=
                g
                |>FGraph.toSeq

            let copiedGraph :FGraph<'Node,'Label*int,'Edge> =
                graphSeq
                |>Seq.map(fun (nk1,nd1,nk2,nd2,e) ->
                    nk1,(nd1,(nodeToCommunity.Item nk1)),
                    nk2,(nd2,(nodeToCommunity.Item nk2)),
                    e
                )
                |>FGraph.ofSeq

            let copiedGraph2:FGraph<int,int*int,'Edge> =
                graphSeq
                |>Seq.map(fun (nk1,nd1,nk2,nd2,e) ->
                    nodeToCommunity.Item nk1,(nodeToCommunity.Item nk1,nodeToCommunity.Item nk1),
                    nodeToCommunity.Item nk2,(nodeToCommunity.Item nk2,nodeToCommunity.Item nk2),
                    e
                )
                |>FGraph.ofSeq

            let louvainCycleInPlace (graph:FGraph<int,int*int,float>) (randomized:bool) (modularityIncreaseThreshold: float) (numberOfLoops:int) (previousModularity:float) :(int*FGraph<int,int*int,float>*float)=
                    
                //Array of all vertices in the graph
                let verti =
                    graph.Keys|>Array.ofSeq

                //Shuffles the verti array if radomize is true
                if randomized then
                    shuffle verti|>ignore


                //Array of all neighbouring vertices, returned as (vertex,edgeweight) array. The index of the element is the same as the vertex in verti.
                let neighbours =
                        verti
                        |> Array.map(fun x -> graph.Item x|>FContext.neighbours|>Seq.distinct|>Array.ofSeq)

                            
                //weighted Degree of the vertex. The index of the element is the same as the vertex in verti.
                let ki =
                    neighbours|>Array.map(fun adj -> Seq.sumBy snd adj)
                
                
                //Total weight of all edges combined
                let totalWeight =      
                    ki
                    |>Seq.sum
                

                //The weight of all self-referencing loops of the vertices. The index of the element is the same as the vertex in verti.
                let selfLoops =                                                
                    neighbours
                    |>Array.mapi(fun i adj ->
                        adj|> Seq.sumBy (fun (v,w) -> if v=(verti.[i]) then w else 0.)
                    )

                //A Dictionary, where the key is the community and the value is a tupel of the weighted degree of the community and the sum of all internal edges.
                let communitySumtotalSumintern =
                    let output = System.Collections.Generic.Dictionary<int,float*float>() 
                    for i=0 to (FGraph.countNodes graph)-1 do
                        let vertex = verti.[i]
                        let originalLabel,label = graph.Item vertex|>fun (_,l,_) -> l
                        let communityWeightTotalStart =  ki.[i]
                        let selfLoopsStart = selfLoops.[i] 
                        output.Add(label,(communityWeightTotalStart,selfLoopsStart))
                    output       
                
                //Function to calculate the modularity of the graph.
                let modularityQuality resolution =
                    let mutable q = 0.
                    for i in communitySumtotalSumintern do
                        let (totalSumC,sumIntern) = i.Value
                        if totalSumC > 0. then 
                            let calculation = resolution*((sumIntern/2.)/(totalWeight/2.))-((totalSumC/totalWeight)**2.)

                            q <- (q+(calculation))
                    q

                let newModQ resolution :float =
                    let f = resolution
                    let mutable q = 0. 
                    for i in communitySumtotalSumintern do
                        let (totalSumC,sumIntern) = i.Value
                        if totalSumC > 0. then 
                            let calculation = ((sumIntern)-(totalSumC*totalSumC) / totalWeight)
                    
                            q <- (q+(calculation))

                    (q/totalWeight)
                        
                //Minimal increase in modularity Quality that has to be achieved. If the increase in modularity is lower, then the first phase of the louvain Algorithm ends and a new iteration can begin.
                let increaseMin = modularityIncreaseThreshold //0.000001

                //Runs once over all vertices in the graph and move the vertex into the community to which the modularity gain is maximal. In case of no positive gain, the original community is kept.
                let rec louvainOneLevel (counter:int) (nbOfMoves:int) =
                    
                    //Do until
                    if counter = FGraph.countNodes graph then 

                        nbOfMoves > 0

                    else            
                        
                        //Vertex that is looked at.
                        let node                                 = verti.[counter]
                        
                        //The weighted degree of the node.
                        let ki                                   = ki.[counter] 

                        //The weight of all self-referencing loops of the vertex.
                        let selfloopNode                         = selfLoops.[counter]
                        
                        //Community of the node before potential improvement.
                        let _,(fixedCommunity,originalCommunity),_   = (graph.Item node) 

                        //Weighted degree of the community,the sum of all internal edges.
                        let (originalCommunityTotalSum,originalCommunitySumIntern)       = getValue originalCommunity communitySumtotalSumintern
                                
                        //Remove node from its original community.                   
                        FGraph.setNodeData node (fixedCommunity,-1) graph |> ignore

                        //All neighbors of the node with their edgeWeight.         
                        let neighbors           = 
                        
                            neighbours.[counter]
                            |> Array.filter (fun (vertex,weight) -> vertex <> node) 


                        // printf $"{node} {ki} {selfloopNode} {originalCommunityTotalSum} {originalCommunitySumIntern} "|>ignore
                        // neighbors|>Array.map(fun (n,v) -> printfn " %i  %f"n v)|>ignore
                        // printfn " "|>ignore

                        //This if condition prevents problems If the node is isolated and has 0 edges. 
                        if neighbors = Array.empty then  
                            
                            FGraph.setNodeData node (fixedCommunity, originalCommunity) graph|> ignore
                            louvainOneLevel (counter+1) (nbOfMoves)


                        else
                                        
                            //All communities the node is connected to with their edgeweight.
                            let connectedCommunities     = 
                                                    
                                neighbors
                                |> Array.map (fun (vertex,weight) -> (((graph.Item vertex)|>fun (_,(l1,l2),_) -> l2),weight)) 
                            
                            //All communities the node is connected to with their edgeweight, removing duplicates. 
                            let connectedCommunitiesCondensed =
                            
                                sumGroupBy fst snd connectedCommunities        
                            
                            //All weights to the original community of the node.
                            let weightofConnectionToOldCommunity         =   
                            
                                findWeightofConnectionToOldCommunity connectedCommunitiesCondensed originalCommunity

                            //Removing the node from its community, updating community values communityWeightTotal and sumIntern.
                            let communityWeightTotalUpdate =  (originalCommunityTotalSum-ki)
                            let sumInternUpdate            =  (originalCommunitySumIntern-((2.*(weightofConnectionToOldCommunity))+(selfloopNode)))                  

                            communitySumtotalSumintern.Item originalCommunity <- (communityWeightTotalUpdate,sumInternUpdate)

                            let connectedCommunitiesCondensedNew =
                                Array.append [|originalCommunity,weightofConnectionToOldCommunity|] connectedCommunitiesCondensed
                                |> Array.distinct
                            
                            //connectedCommunitiesCondensedNew|>Array.map(fun (i,x) -> printfn"%i %f" i x)

                            //printfn "EndOfIF"

                            //Calculating the best possible community for the node, based on modularity gain. 
                            //Outputs the bestCommunity, the gain acived by moving the node to that community and the weight of the connection to that new Community.  
                            let (bestCommunity,modularityGain,connectionToBestCommunity) =                        

                                let calculations = 
                                    connectedCommunitiesCondensedNew
                                    |> Array.map (fun (community,connectionToCommunity) -> 
                                            (
                                            //printfn $"{community} {(resolution*connectionToCommunity-((Dictionary.getValue community communitySumtotalSumintern|>fst)*ki/totalWeight))} {connectionToCommunity}"
                                            community,
                                            (resolution*connectionToCommunity-((getValue community communitySumtotalSumintern|>fst)*ki/totalWeight)),
                                            connectionToCommunity
                                            )
                                        )

                                calculations
                                |> Array.maxBy (fun (community,modularityGain,connectionToCommunity) -> modularityGain)
                            //printfn "%i %f" bestCommunity modularityGain
                            //If there is a gain in modularity bigger than 0.
                            if modularityGain < 0.  then 
                                //printfn "mod<0"
                                //Resetting the community to its original state.                       
                                FGraph.setNodeData node (fixedCommunity,originalCommunity) graph|> ignore
                                communitySumtotalSumintern.Item originalCommunity <- (originalCommunityTotalSum,originalCommunitySumIntern)
                        
                                louvainOneLevel (counter+1) (nbOfMoves)

                            else                                           
                                //printfn "mod>0"

                                let (communityNewSum,communityNewIn) = getValue bestCommunity communitySumtotalSumintern

                                //Moving the node to its new community.
                                let sumInternBestCommunity              =      (communityNewIn+((2.*(connectionToBestCommunity)+(selfloopNode))))
                                let communityWeightTotalBestCommunity   =      (communityNewSum+ki)
                            
                                FGraph.setNodeData node (fixedCommunity,bestCommunity) graph|> ignore
                                communitySumtotalSumintern.Item bestCommunity <- (communityWeightTotalBestCommunity,sumInternBestCommunity)

                                (if bestCommunity <> originalCommunity then (nbOfMoves+1) else nbOfMoves)
                                |> louvainOneLevel (counter+1) 
            
                //A loop that executes louvainOneLevel as long as none of the exit conditions are met.
                //The exit conditions are
                // 1) No improvement was preformed 
                // 2) The increase in modularityQuality by preforming the louvainOneLevel results in a score lower than the increaseMin.
                let rec loop nbOfMoves currentQuality improvement :(int*FGraph<int,int*int,'Edge>*float)=

                    let qualityNew = modularityQuality resolution

                    let build (shouldIBuild:bool) :int*FGraph<int,int*int,'Edge>*float=

                        if not shouldIBuild then
                            failwith "ERROR"
                        else
                        
                        //Returns a Map oldCommunity -> updatedCommunity; Returns a dictionary where the key is the vertex and the value is the new community
                            let (vertexToLabelMap,vertexNewLabel) :((Map<int,int>)*(Dictionary<int,int>))=
                                let labelC = graph.Values|> Seq.map (fun (_,(l1,l2),_) -> l1,l2)
                                let labelMap =                           
                                    labelC
                                    |>Seq.map snd
                                    |> Seq.distinct
                                    |> Seq.mapi (fun i label -> (label,i))
                                    |> Map.ofSeq
                                let labelMap2 = 
                                    [|
                                        for (oldCommunity,newCommunity) in labelC do
                                            oldCommunity,labelMap.[newCommunity]
                                    |]
                                    |> Map.ofArray

                                let vertexDict = System.Collections.Generic.Dictionary<int,int>()
                                for i in verti do
                                    vertexDict.Add (i,(labelMap.[(graph.Item i|>fun (_,(l1,l2),_) -> l2)]))

                                labelMap2,vertexDict                         
                            
                            //Updates the second set of labels in the outputgraph
                            for i in copiedGraph.Keys do
                                let (originalLabel,currentLabel) = copiedGraph.Item i|> fun (_,s,_) ->s
                                let updateLabel     = vertexToLabelMap.[currentLabel]
                                FGraph.setNodeData i (originalLabel,updateLabel) copiedGraph
                                |> ignore
                            
                            //Return the edgeList for the next iteration of the louvain algorithm.
                            let elementSeq :('Nk1*'Nd1*'Nk2*'Nd2*float)seq=

                                let getLabel vertex =
                                    getValue vertex vertexNewLabel
                                

                                FGraph.toEdgeSeq graph
                                |> Seq.map (fun (s,t,w) -> ((getLabel s),(getLabel t),w))
                                |> Seq.groupBy(fun (s,t,w) ->
                                    if s<t then 
                                        (s,t)
                                    else
                                        (t,s)
                                )
                                |> Seq.map(fun ((s,t),toSum) -> 
                                    s,(s,s),
                                    t,(t,t),
                                    toSum
                                    |>Seq.sumBy (fun (s,t,w) -> w))

                                

                            nbOfMoves,                                    
                            FGraph.ofSeq elementSeq,
                            qualityNew
                    
                    //Start of the cycle
                    if nbOfMoves = 0 then 

                        let hasImProved = louvainOneLevel 0 0
                        
                        loop (nbOfMoves+1) currentQuality hasImProved
            
                    
                    elif improvement then 
                        
                        if (qualityNew-currentQuality) > increaseMin then 

                            loop (nbOfMoves+1) (qualityNew) (louvainOneLevel 0 0)

                        else                    

                            build true


                    elif numberOfLoops > 0 && currentQuality < previousModularity then

                        nbOfMoves,
                        graph,
                        qualityNew

                    elif improvement = false && nbOfMoves = 1 then 

                        nbOfMoves,
                        graph,
                        qualityNew

                    else 

                        build true
                        
                //Start the louvainApplication
                loop 0 (modularityQuality resolution) false

            //The louvainLoop combines the two phases of the louvain Algorithm. As long as improvments can be performed, the louvainApplication is executed.
            let rec louvainInPlace_ nbOfLoops (newG:FGraph<int,int*int,'Edge>) (modularityIncreaseThreshold: float) (modulartiy:float) =
            
                let (nbOfMoves,newGraph,newModularity) = 
                
                    louvainCycleInPlace newG randomized modularityIncreaseThreshold nbOfLoops modulartiy           

                if nbOfMoves < 2 || ((nbOfLoops>0) && (newModularity<modulartiy)) then 
                    

                    printfn "new modularity= %A" modulartiy
                    copiedGraph

                else 

                    louvainInPlace_ (nbOfLoops+1) newGraph modularityIncreaseThreshold newModularity


            louvainInPlace_ 0 copiedGraph2 modularityIncreaseThreshold 0.
        louvainMethod graph randomized modularityIncreaseThreshold resolution


    /// <summary> 
    /// Takes a FGraph and returns a new graph whose NodeData has been transformed into tupels, where the second part is the community according to modularity-optimization by the Louvain Algorithm (Blondel, Vincent D; Guillaume, Jean-Loup; Lambiotte, Renaud; Lefebvre, Etienne (9 October 2008). "Fast unfolding of communities in large networks". Journal of Statistical Mechanics: Theory and Experiment. 2008 ).
    /// </summary>
    /// <param name="modularityIncreaseThreshold">Threshold of modularity-difference that has to be exceeded in order to be recognized as a modularity-increase. The value has to be between 0. and 1. to get a meaningful result. The smaller the value, the longer the calculation takes.</param> 
    /// <param name="graph">FGraph, that is used as the template for the modularity optimization.</param> 
    /// <returns>A new FGraph whose NodeData has been transformed into tupels, where the second part is the community accorging to modularity-optimization.</returns>
    static member louvain (modularityIncreaseThreshold: float) (graph:FGraph<'Node,'Label,'Edge>) : (FGraph<'Node,'Label*'Community,'Edge>) =
        Louvain.louvainResolution false modularityIncreaseThreshold 1. graph 

    /// <summary> 
    /// Takes a FGraph and returns a new graph whose NodeData has been transformed into tupels, where the second part is the community according to modularity-optimization by the Louvain Algorithm (Blondel, Vincent D; Guillaume, Jean-Loup; Lambiotte, Renaud; Lefebvre, Etienne (9 October 2008). "Fast unfolding of communities in large networks". Journal of Statistical Mechanics: Theory and Experiment. 2008 ).
    /// </summary>
    /// <param name="modularityIncreaseThreshold">Threshold of modularity-difference that has to be exceeded in order to be recognized as a modularity-increase. The value has to be between 0. and 1. to get a meaningful result. The smaller the value, the longer the calculation takes.</param> 
    /// <param name="graph">FGraph, that is used as the template for the modularity optimization.</param> 
    /// <returns>A new FGraph whose NodeData has been transformed into tupels, where the second part is the community accorging to modularity-optimization.</returns>
    static member louvainRandom (modularityIncreaseThreshold: float) (graph:FGraph<'Node,'Label,'Edge>) : (FGraph<'Node,'Label*'Community,'Edge>)=
        Louvain.louvainResolution true modularityIncreaseThreshold 1. graph 
        
    
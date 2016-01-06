﻿namespace NoGriefPlugin.ProcessHandlers
{
    using NoGriefPlugin.Settings;
    using NoGriefPlugin;
    using VRageMath;
    using VRage.ModAPI;
    using Sandbox.ModAPI;
    using System.Collections.Generic;
    using Sandbox.Game.Entities;
    using SEModAPIInternal.API.Common;
    using NoGriefPlugin.Utility;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.Common;
    using System;
    using VRage.ObjectBuilders;
    using Sandbox.Engine.Multiplayer;
    using Sandbox.Game.Replication;
    using Sandbox.Game.World;
    using Sandbox.Game.Multiplayer;
    using Sandbox.Game.Entities.Character;
    using Sandbox.Definitions;
    using Sandbox.Game.Gui;
    public class ProcessExclusionZone : ProcessHandlerBase
    {
        private static bool _init = false;
        public static SortedList<long, HashSet<long>> WarnList = new SortedList<long, HashSet<long>>( );

        public override int GetUpdateResolution( )
        {
            return 1000;
        }

        public override void Handle( )
        {
            if ( PluginSettings.Instance.ExclusionEnabled )
            {

                if ( !_init )
                    Init( );

                foreach ( ExclusionItem item in PluginSettings.Instance.ExclusionItems )
                {
                    if ( item.Enabled )
                    {
                        if ( item.AllowedEntities.Count < 1 )
                            InitItem( item );

                        bool updateWarn = false;
                        HashSet<long> ItemWarnList;
                        if ( !WarnList.TryGetValue( item.EntityId, out ItemWarnList ) )
                        {
                            ItemWarnList = new HashSet<long>( );
                            WarnList.Add( item.EntityId, ItemWarnList );
                        }

                        IMyEntity itemEntity;
                        if ( !MyAPIGateway.Entities.TryGetEntityById( item.EntityId, out itemEntity ) )
                        {
                            if ( PluginSettings.Instance.ExclusionLogging )
                                NoGrief.Log.Info( "Error processing protection zone on entity {0}, could not get entity.", item.EntityId );
                            item.Enabled = false;
                            continue;
                        }
                        if ( itemEntity.Physics.LinearVelocity != Vector3D.Zero || itemEntity.Physics.AngularVelocity != Vector3D.Zero )
                        {
                            if ( PluginSettings.Instance.ExclusionLogging )
                                NoGrief.Log.Debug( "Not processing protection zone on entity {0} -> {1} because it is moving.", item.EntityId, itemEntity.DisplayName );
                            continue;
                        }

                        //create the actual protection zone
                        BoundingSphereD protectSphere = new BoundingSphereD( itemEntity.GetPosition( ), item.ExclusionRadius );
                        List<IMyEntity> protectEntities = MyAPIGateway.Entities.GetEntitiesInSphere( ref protectSphere );

                        //create a second sphere 100m larger, this is our boundary zone
                        protectSphere = new BoundingSphereD( itemEntity.GetPosition( ), item.ExclusionRadius + 100 );
                        List<IMyEntity> excludedEntities = MyAPIGateway.Entities.GetEntitiesInSphere( ref protectSphere );

                        //check entities in our warning list
                        foreach ( long warnId in ItemWarnList )
                        {
                            IMyEntity warnEntity;
                            if ( !MyAPIGateway.Entities.TryGetEntityById( warnId, out warnEntity ) )
                            {
                                //entity no longer exists, remove it from the list
                                ItemWarnList.Remove( warnId );
                                updateWarn = true;
                                continue;
                            }

                            if ( PluginSettings.Instance.ExclusionLogging )
                                NoGrief.Log.Debug( "Processing WarnList. Entity type: " + warnEntity.GetType( ).ToString( ) );
                            if ( !excludedEntities.Contains( warnEntity ) )
                            {
                                //entity has left the exclusion zone
                                ItemWarnList.Remove( warnId );
                                updateWarn = true;
                                if ( warnEntity is IMyCharacter )
                                {
                                    if ( PluginSettings.Instance.ExclusionLogging )
                                        NoGrief.Log.Debug( "Player left exclusion zone" );
                                    MyCharacter player = (MyCharacter)warnEntity;
                                    ulong steamID = PlayerMap.Instance.GetSteamIdFromPlayerName( player.DisplayName );
                                    Communication.Notification( steamID, MyFontEnum.Green, 3, "Left exclusion zone" );
                                }
                                else if ( warnEntity is IMyCubeGrid )
                                {
                                    //I'll do this later

                                    if ( PluginSettings.Instance.ExclusionLogging )
                                        NoGrief.Log.Debug( "Ship left exclusion zone" );
                                    ItemWarnList.Remove( warnId );
                                    updateWarn = true;
                                }
                            }
                            if ( protectEntities.Contains( warnEntity ) )
                            {
                                //object has moved from boundary into protection zone
                                if ( warnEntity is IMyCharacter )
                                {
                                    if ( PluginSettings.Instance.ExclusionLogging )
                                        NoGrief.Log.Debug( "Found player in protection zone" );
                                    MyCharacter player = (MyCharacter)warnEntity;
                                    updateWarn = true;
                                    ItemWarnList.Remove( warnId );
                                    ulong steamID = PlayerMap.Instance.GetSteamIdFromPlayerName( player.DisplayName );
                                    if ( player.IsUsing is IMyCubeBlock )
                                    {
                                        //player is in a ship, they'll get moved along with it, simply notify them here
                                        Communication.Notification( steamID, MyFontEnum.Red, 3, "You have been removed from the protection zone." );
                                        continue;
                                    }
                                    Vector3D? tryMove = MathUtility.TraceVector( warnEntity.GetPosition( ), warnEntity.Physics.LinearVelocity, -100 );
                                    if ( tryMove == null )
                                    {

                                        if ( PluginSettings.Instance.ExclusionLogging )
                                            NoGrief.Log.Debug( "Failed to move player" );
                                    }

                                    if ( PluginSettings.Instance.ExclusionLogging )
                                        NoGrief.Log.Debug( "Moving player" );
                                    Communication.MoveMessage( steamID, "normal", (Vector3D)tryMove );
                                    Communication.Notification( steamID, MyFontEnum.Red, 3, "You have been removed from the protection zone." );
                                }

                                if ( warnEntity is IMyMissileGunObject )
                                {
                                    SandboxGameAssemblyWrapper.Instance.GameAction( ( ) =>
                                    {
                                        warnEntity.Close( );
                                        MyMultiplayer.ReplicateImmediatelly( MyExternalReplicable.FindByObject( warnEntity ) );
                                        //this replication shouldn't be necessary, but let's force a sync just to be safe.
                                    } );
                                    ItemWarnList.Remove( warnId );
                                    updateWarn = true;
                                    continue;
                                }


                                if ( warnEntity is IMyCubeGrid )
                                {
                                    updateWarn = true;
                                    ItemWarnList.Remove( warnId );
                                    Vector3D? tryMove = MathUtility.TraceVector( warnEntity.GetPosition( ), warnEntity.Physics.LinearVelocity, -100 );
                                    if ( tryMove == null )
                                    {
                                        //do something
                                    }
                                    Communication.MoveMessage( 0, "normal", (Vector3D)tryMove, warnEntity.GetTopMostParent( ).EntityId );
                                    continue;
                                }

                                if ( warnEntity is IMyFloatingObject )
                                {
                                    SandboxGameAssemblyWrapper.Instance.GameAction( ( ) =>
                                    {
                                        warnEntity.Close( );
                                        MyMultiplayer.ReplicateImmediatelly( MyExternalReplicable.FindByObject( warnEntity ) );
                                        //this replication shouldn't be necessary, but let's force a sync just to be safe.
                                    } );
                                    ItemWarnList.Remove( warnId );
                                    updateWarn = true;
                                    continue;
                                }
                            }
                        }

                        if ( excludedEntities.Count == protectEntities.Count )
                            continue;
                        

                        foreach ( IMyEntity entity in excludedEntities )
                        {
                            if ( PluginSettings.Instance.ExclusionLogging && entity != null )
                            {
                                if ( PluginSettings.Instance.ExclusionLogging )
                                    NoGrief.Log.Info( entity.GetType( ).ToString( ) );
                            }

                            //ignore items in the protected sphere so we only process the boundary zone
                            if ( protectEntities.Contains( entity ) )
                                continue;

                            if ( entity == null )
                                continue;

                            if ( entity is IMyCubeGrid )
                            {
                                if ( PluginSettings.Instance.ExclusionLogging )
                                    NoGrief.Log.Debug( "Found ship in exclusion zone" );
                                IMyCubeGrid grid = (IMyCubeGrid)entity;
                                MyObjectBuilder_CubeGrid gridBuilder = (MyObjectBuilder_CubeGrid)grid.GetObjectBuilder( );
                                HashSet<ulong> playersOnboard = new HashSet<ulong>( );

                                if ( item.AllowedEntities.Contains( entity.EntityId ) && !item.TransportAdd )
                                    continue;

                                foreach ( MyObjectBuilder_CubeBlock block in gridBuilder.CubeBlocks )
                                {
                                    if ( block.TypeId == typeof( MyObjectBuilder_Cockpit ) )
                                    {
                                        MyObjectBuilder_Cockpit cockpit = (MyObjectBuilder_Cockpit)block;
                                        if ( cockpit.Pilot == null )
                                            continue;

                                        ulong steamId = PlayerMap.Instance.GetSteamIdFromPlayerName( cockpit.Pilot.DisplayName );
                                        if ( !playersOnboard.Contains( steamId ) )
                                            playersOnboard.Add( steamId );

                                        if ( !ItemWarnList.Contains( cockpit.Pilot.EntityId ) )
                                            ItemWarnList.Add( cockpit.Pilot.EntityId );
                                    }
                                }

                                //get the pilot of the ship if they aren't in the list already
                                //this probably means they're remote controlling the ship, should we do something about that?
                                /*  IMyControllableEntity ship = (IMyControllableEntity)entity;
                                  MyControllerInfo shipController = ship.ControllerInfo;
                                  ulong controlSteamId = PlayerMap.Instance.GetSteamIdFromPlayerId( shipController.ControllingIdentityId );

                                  if ( !playersOnboard.Contains( controlSteamId ) )
                                      playersOnboard.Add( controlSteamId );

                                  if ( item.AllowedEntities.Contains( entity.EntityId ) && item.TransportAdd )
                                  {
                                      foreach ( ulong steamId in playersOnboard )
                                      {
                                          if ( !item.AllowedPlayers.Contains( steamId ) )
                                              item.AllowedPlayers.Add( steamId );
                                      }
                                      continue;
                                  }*/
                                //there's probably a faster way to do this, but the number of items is usually pretty low, so whatever
                                bool found = false;
                                foreach ( ulong steamId in playersOnboard )
                                {
                                    if ( PluginSettings.Instance.ExclusionLogging )
                                        NoGrief.Log.Debug( "Warning player" );
                                    //send the user a warning message
                                    Communication.Notification( steamId, MyFontEnum.Red, 3, "Warning: Approaching exclusion zone" );

                                    if ( item.AllowedPlayers.Contains( steamId ) )
                                    {
                                        found = true;
                                        break;
                                    }
                                    if ( item.AllowAdmins && PlayerManager.Instance.IsUserAdmin( steamId ) )
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if ( found && item.TransportAdd )
                                {
                                    foreach ( ulong steamId in playersOnboard )
                                    {
                                        if ( !item.AllowedPlayers.Contains( steamId ) )
                                            item.AllowedPlayers.Add( steamId );
                                    }
                                    continue;
                                }
                                else if ( found )
                                    continue;
                                else
                                {
                                    if ( ItemWarnList.Contains( entity.EntityId ) )
                                        continue;
                                    ItemWarnList.Add( entity.EntityId );
                                    updateWarn = true;

                                    //oh hey look we FINALLY have a ship we need to stop.
                                    foreach ( ulong steamId in playersOnboard )
                                        Communication.Notification( steamId, MyFontEnum.Red, 3, "Warning: Approaching exclusion zone" );
                                    /*
                                    //move the ship some multiple of 100m back the direction it came
                                    Vector3D? tmpVect = MathUtility.TraceVector( entity.GetPosition( ), entity.Physics.LinearVelocity, -100, 10 );
                                    Vector3D moveTo;
                                    if ( tmpVect != null )
                                        moveTo = (Vector3D)tmpVect;
                                    else
                                    {
                                        //couldn't find anywhere to put the player.
                                        //do something about it?
                                        continue;
                                    }
                                    //stop the ship and broadcast the move message to clients
                                    //GetTopMostParent should move the ship and any subgrids, as well as items attached by landing gear
                                    Communication.MoveMessage( 0, "normal", moveTo, entity.GetTopMostParent( ).EntityId );
                                    entity.GetTopMostParent().Physics.LinearVelocity = Vector3D.Zero;
                                    entity.GetTopMostParent().Physics.AngularVelocity = Vector3D.Zero;
                                */
                                }
                            }

                            if ( entity is IMyFloatingObject )
                            {
                                if ( PluginSettings.Instance.ExclusionLogging )
                                    NoGrief.Log.Debug( "Found floating object in exclusion zone" );
                                //floating object, has someone hurled a huge rock at us?
                                if ( entity.Physics.LinearVelocity.Length( ) > 10f )
                                {
                                    //should we delete or just stop fast moving floaters?
                                    entity.Physics.LinearVelocity = Vector3D.Zero;
                                    entity.Physics.AngularVelocity = Vector3D.Zero;

                                    /*this will delete the object
                                    SandboxGameAssemblyWrapper.Instance.GameAction( ( ) =>
                                    {
                                        entity.Close( );
                                        MyMultiplayer.ReplicateImmediatelly( MyExternalReplicable.FindByObject( entity ) );
                                    //this replication shouldn't be necessary, but let's force a sync just to be safe.
                                    } );*/
                                }
                                continue;
                            }

                            if ( entity is IMyCharacter )
                            {
                                if ( PluginSettings.Instance.ExclusionLogging )
                                    NoGrief.Log.Debug( "Found player in exclusion zone" );
                                MyCharacter player = (MyCharacter)entity;
                                ulong steamID = PlayerMap.Instance.GetSteamIdFromPlayerName( player.DisplayName );
                                if ( item.AllowedPlayers.Contains( steamID ) )
                                    continue;
                                else if ( item.AllowAdmins && PlayerManager.Instance.IsUserAdmin( steamID ) )
                                    continue;
                                else
                                {
                                    if ( ItemWarnList.Contains( entity.EntityId ) )
                                        continue;
                                    ItemWarnList.Add( entity.EntityId );
                                    updateWarn = true;
                                    if ( PluginSettings.Instance.ExclusionLogging )
                                        NoGrief.Log.Debug( "Warning player" );
                                    //send the user a warning message
                                    Communication.Notification( steamID, MyFontEnum.Red, 3, "Warning: Approaching exclusion zone" );
                                    /*
                                    //move the player some multiple of 100m back the direction they came
                                    Vector3D? tmpVect = MathUtility.TraceVector( entity.GetPosition( ), entity.Physics.LinearVelocity, -100, 10 );
                                    Vector3D moveTo;
                                    if ( tmpVect != null )
                                        moveTo = (Vector3D)tmpVect;
                                    else
                                    {
                                        //couldn't find anywhere to put the player.
                                        //do something about it?
                                        continue;
                                    }
                                    entity.Physics.LinearVelocity = Vector3D.Zero;
                                    entity.Physics.AngularVelocity = Vector3D.Zero;

                                    //tell the client to move the player
                                    Communication.MoveMessage( player.SteamUserId, "normal", moveTo );
                                    */
                                }
                            }

                        }

                        protectEntities.Clear( );
                        excludedEntities.Clear( );
                        if ( updateWarn )
                        {
                            WarnList.Remove( item.EntityId );
                            WarnList.Add( item.EntityId, ItemWarnList );
                        }
                    }
                }
                base.Handle( );
            }
        }

        private void Init( )
        {
            _init = true;
            foreach ( ExclusionItem item in PluginSettings.Instance.ExclusionItems )
            {
                InitItem( item );
                //initialize our list of warned items
                WarnList.Add( item.EntityId, new HashSet<long>( ) );
            }
        }

        public static void InitItem( ExclusionItem item )
        {
            if ( !PluginSettings.Instance.ExclusionEnabled )
                return;

            IMyEntity itemEntity;
            if ( PluginSettings.Instance.ExclusionLogging )
                NoGrief.Log.Debug( "Initializing protection zone on entity {0}", item.EntityId );

            if ( !MyAPIGateway.Entities.TryGetEntityById( item.EntityId, out itemEntity ) )
            {
                if ( PluginSettings.Instance.ExclusionLogging )
                    NoGrief.Log.Info( "Couldn't initialize protection zone on entity {0}", item.EntityId );
                item.Enabled = false;
                return;
            }
            if ( itemEntity.Physics.LinearVelocity != Vector3D.Zero || itemEntity.Physics.AngularVelocity != Vector3D.Zero )
            {
                if ( PluginSettings.Instance.ExclusionLogging )
                    NoGrief.Log.Info( "Couldn't initialize protection zone on entity {0} -> {1}, entity is moving.", item.EntityId, itemEntity.DisplayName );
                return;
            }

            BoundingSphereD protectSphere = new BoundingSphereD( itemEntity.GetPosition( ), item.ExclusionRadius );
            List<IMyEntity> protectEntities = MyAPIGateway.Entities.GetEntitiesInSphere( ref protectSphere );
            foreach ( IMyEntity entity in protectEntities )
            {
                if ( entity is IMyCubeGrid )
                {
                    long entityId = entity.EntityId;
                    if ( !item.AllowedEntities.Contains( entityId ) )
                        item.AllowedEntities.Add( entityId );
                    List<long> ownerList = new List<long>( );

                    try
                    {
                        IMyCubeGrid tmpGrid = (IMyCubeGrid)entity;
                        ownerList = tmpGrid.BigOwners;
                    }
                    catch ( Exception ex )
                    {
                        if ( PluginSettings.Instance.ExclusionLogging )
                            NoGrief.Log.Info( ex, "Couldn't get owner list for entity " + entityId.ToString( ) );
                        continue;
                    }
                    foreach ( long playerID in ownerList )
                    {
                        ulong steamID;

                        //there's probably a better way to do this, but here's a bandaid for now
                        try
                        {
                            steamID = PlayerMap.Instance.GetSteamIdFromPlayerId( playerID );
                        }
                        catch
                        {
                            //owner is an NPC
                            continue;
                        }

                        if ( !item.AllowedPlayers.Contains( steamID ) )
                            item.AllowedPlayers.Add( steamID );
                    }
                }

                if ( entity is IMyPlayer )
                {
                    IMyPlayer player = (IMyPlayer)entity;
                    ulong steamId = player.SteamUserId;

                    if ( !item.AllowedPlayers.Contains( steamId ) )
                        item.AllowedPlayers.Add( steamId );
                }
            }
        }
    }
}
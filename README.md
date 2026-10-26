# GiddyUp2
This is a code-refresh for Giddy-Up. Hugslib removed, peer reviewed for performance, and remade into an all-in-one design for its modules.
Check the wiki for extension documentation: https://github.com/Owlchemist/GiddyUp2/wiki

https://steamcommunity.com/sharedfiles/filedetails/?id=2934245647

## Take Flight

Riders on flying mounts ignore terrain costs, can take flight to a spot on the map, and can fly across the world map. Animals that fly in vanilla and Vanilla Expanded floating creatures are detected automatically; everything else can be switched on in the Take Flight settings tab.

Mark an animal as a flying mount with the `GiddyUpTakeFlight.FlyingMount` mod extension. Every field is optional; leave one out to use the mod setting.

```xml
<li Class="GiddyUpTakeFlight.FlyingMount">
  <canFly>true</canFly>                       <!-- false stops an auto-detected animal from flying -->
  <rangeFactor>1.2</rangeFactor>              <!-- multiplies the Take flight range on the map -->
  <cooldownFactor>0.8</cooldownFactor>        <!-- multiplies the rest time after a flight -->
  <worldFlightRange>12</worldFlightRange>     <!-- world map range in tiles at full load, 0 = no world flight -->
  <maxRangeMultiplier>3</maxRangeMultiplier>  <!-- cap on the light-load range bonus -->
  <worldFlightSpeed>120</worldFlightSpeed>    <!-- tiles per day -->
  <rangeByLoad>                               <!-- replaces capacity / load. x = load / capacity, y = range multiplier -->
    <points>
      <li>(0.25, 3)</li>
      <li>(1, 1)</li>
    </points>
  </rangeByLoad>
</li>
```

World range = base range × (carrying capacity ÷ load), never below ×1 and capped at the max multiplier. Load is the rider's mass (including gear and inventory) plus the mount's own inventory; a mount carrying more than its capacity can't fly at all.

Flying mounts without a rider fly along with a group of riders: from a map, select them together with the riders; from a caravan, they come along on their own. The group needs at least one rider, and a riderless mount's range (load is just its own inventory) counts towards the group's range.

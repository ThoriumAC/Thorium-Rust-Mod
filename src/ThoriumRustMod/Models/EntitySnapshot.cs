using System.Collections.Generic;

namespace ThoriumRustMod.Models;

public class EntitySnapshot
{
    private List<Entity> entities { get; set; } = [];
}

public class Entity
{
    public bool entityCreate { get; set; }
    public long netId { get; set; }
    
    public string ownerId { get; set; }
    public int prefabId { get; set; }
    public string prefabName { get; set; }
    
    public float posX { get; set; }
    public float posY { get; set; }
    public float posZ { get; set; }
    
    public float rotX { get; set; }
    public float rotY { get; set; }
    public float rotZ { get; set; }
    
    public float centX { get; set; }
    public float centY { get; set; }
    public float centZ { get; set; }
    
    public float boundsX { get; set; }
    public float boundsY { get; set; }
    public float boundsZ { get; set; }
}
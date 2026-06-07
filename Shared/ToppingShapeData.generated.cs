// Auto-generated topping shape seed. Replace by running ToppingProfileExporter.cs from the Godot editor.
public static partial class ToppingShapeData
{
    public static ToppingShapeProfile GetProfile(string name)
        => name switch
        {
            "Bottom Bread" => new ToppingShapeProfile("Bottom Bread", 1.5f, 1.5f, 0.35f, 1.8f, 0.96f, 1.1f),
            "Lettuce" => new ToppingShapeProfile("Lettuce", 1.25f, 1.25f, 0.18f, 0.65f, 0.88f, 0.92f),
            "Tomato" => new ToppingShapeProfile("Tomato", 1.1f, 1.1f, 0.26f, 1.25f, 0.83f, 0.86f),
            "Cheese" => new ToppingShapeProfile("Cheese", 1.35f, 1.35f, 0.16f, 0.9f, 0.91f, 0.96f),
            "Top Bread" => new ToppingShapeProfile("Top Bread", 1.5f, 1.5f, 0.35f, 1.5f, 0.95f, 1.05f),
            _ => Default,
        };
}

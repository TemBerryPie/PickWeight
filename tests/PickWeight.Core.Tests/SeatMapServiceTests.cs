using PickWeight.Core.Models;
using PickWeight.Core.Services;

namespace PickWeight.Core.Tests;

public sealed class SeatMapServiceTests
{
    private static SeatMap CreateMap()
    {
        var map = new SeatMap();
        SeatMapService.AddColumn(map, 3); // 列0：3 座
        SeatMapService.AddColumn(map, 2); // 列1：2 座
        return map;
    }

    [Fact]
    public void AddColumn_DefaultCountMatchesMaxExisting()
    {
        var map = new SeatMap();
        SeatMapService.AddColumn(map, 4);
        var column = SeatMapService.AddColumn(map);
        Assert.Equal(4, column.SeatCount);
        Assert.Equal(4, column.MemberNames.Count);
    }

    [Fact]
    public void Assign_OccupiesSeat_AndReturnsPreviousOccupant()
    {
        var map = CreateMap();
        SeatMapService.Assign(map, 0, 0, "张三");
        var previous = SeatMapService.Assign(map, 0, 0, "李四");

        Assert.Equal("张三", previous);
        Assert.Equal("李四", map.Columns[0].MemberNames[0]);
    }

    [Fact]
    public void ResizeColumn_RemovesSeatsAndReturnsEvictedNames()
    {
        var map = CreateMap();
        SeatMapService.Assign(map, 0, 0, "张三");
        SeatMapService.Assign(map, 0, 2, "王五");

        var removed = SeatMapService.ResizeColumn(map, 0, 2);

        Assert.Equal(["王五"], removed);
        Assert.Equal(2, map.Columns[0].SeatCount);
        Assert.Equal(2, map.Columns[0].MemberNames.Count);
        Assert.Equal("张三", map.Columns[0].MemberNames[0]);
    }

    [Fact]
    public void RemoveColumn_ReturnsAllOccupants()
    {
        var map = CreateMap();
        SeatMapService.Assign(map, 0, 0, "张三");
        SeatMapService.Assign(map, 0, 1, "李四");

        var removed = SeatMapService.RemoveColumn(map, 0);

        Assert.Equal(["张三", "李四"], removed);
        Assert.Single(map.Columns);
    }

    [Fact]
    public void AutoAssign_FillsEmptySeats_AndReturnsUnassigned()
    {
        var map = CreateMap(); // 共 5 座
        var unassigned = SeatMapService.AutoAssign(map, ["张三", "李四", "王五", "赵六", "钱七", "孙八"]);

        Assert.Equal(["孙八"], unassigned);
        Assert.Equal(5, SeatMapService.SeatedNames(map).Count);
        Assert.Contains("张三", SeatMapService.SeatedNames(map));
        Assert.Contains("钱七", SeatMapService.SeatedNames(map));
    }

    [Fact]
    public void AutoAssign_SkipsAlreadySeated()
    {
        var map = CreateMap();
        SeatMapService.Assign(map, 0, 0, "张三");
        var unassigned = SeatMapService.AutoAssign(map, ["张三", "李四"]);

        Assert.Empty(unassigned);
        Assert.Equal("张三", map.Columns[0].MemberNames[0]);
        Assert.Contains("李四", SeatMapService.SeatedNames(map));
    }

    [Fact]
    public void Validate_RejectsDuplicateNames()
    {
        var map = CreateMap();
        SeatMapService.Assign(map, 0, 0, "张三");
        SeatMapService.Assign(map, 1, 0, "张三");

        var error = SeatMapService.Validate(map);

        Assert.NotNull(error);
        Assert.Contains("张三", error);
    }

    [Fact]
    public void Validate_AcceptsValidMap()
    {
        var map = CreateMap();
        SeatMapService.Assign(map, 0, 0, "张三");
        SeatMapService.Assign(map, 1, 0, "李四");

        Assert.Null(SeatMapService.Validate(map));
    }

    [Fact]
    public void MembersInRegion_SpecificRow_SkipsShortColumns()
    {
        var map = CreateMap(); // 列0 有 3 座，列1 有 2 座
        SeatMapService.Assign(map, 0, 0, "张三");
        SeatMapService.Assign(map, 0, 1, "李四");
        SeatMapService.Assign(map, 1, 0, "王五");

        // 第 2 排（下标 1）：列0 有李四；列1 只有 2 座且第 1 排为空
        var result = SeatMapService.MembersInRegion(map, SeatRegion.SpecificRow, rowIndex: 1);

        Assert.Equal(["李四"], result);
    }

    [Fact]
    public void MembersInRegion_DoorHalf_RespectsDoorSide()
    {
        var map = new SeatMap();
        SeatMapService.AddColumn(map, 2);
        SeatMapService.AddColumn(map, 2);
        SeatMapService.AddColumn(map, 2);
        SeatMapService.Assign(map, 0, 0, "A");
        SeatMapService.Assign(map, 1, 0, "B");
        SeatMapService.Assign(map, 2, 0, "C");

        // 靠门侧在左：DoorHalf = 前 2 列（左侧一半）
        Assert.Equal(["A", "B"], SeatMapService.MembersInRegion(map, SeatRegion.DoorHalf));

        // 靠门侧在右：DoorHalf = 后 2 列
        map.DoorSide = SeatSideOrientation.End;
        Assert.Equal(["B", "C"], SeatMapService.MembersInRegion(map, SeatRegion.DoorHalf));
    }

    [Fact]
    public void MembersInRegion_WindowHalf_IsComplementOfDoorHalf()
    {
        var map = new SeatMap();
        SeatMapService.AddColumn(map, 2);
        SeatMapService.AddColumn(map, 2);
        SeatMapService.AddColumn(map, 2);
        SeatMapService.Assign(map, 0, 0, "A");
        SeatMapService.Assign(map, 1, 0, "B");
        SeatMapService.Assign(map, 2, 0, "C");

        var door = SeatMapService.MembersInRegion(map, SeatRegion.DoorHalf);
        var window = SeatMapService.MembersInRegion(map, SeatRegion.WindowHalf);

        Assert.Equal(3, door.Count + window.Count);
        Assert.DoesNotContain(door, name => window.Contains(name));
    }

    [Fact]
    public void ComputeVisualLayout_DoorStart_IsSequentialWithColumnAisles()
    {
        var map = new SeatMap { DoorSide = SeatSideOrientation.Start };
        SeatMapService.AddColumn(map, 1);
        SeatMapService.AddColumn(map, 1);
        SeatMapService.AddColumn(map, 1);
        map.Columns[0].HasAisleAfter = true;
        map.Columns[2].HasAisleAfter = true;

        var layout = SeatMapService.ComputeVisualLayout(map);

        Assert.Equal(3, layout.Count);
        Assert.Equal((0, true), layout[0]);
        Assert.Equal((1, false), layout[1]);
        Assert.Equal((2, true), layout[2]);
    }

    [Fact]
    public void ComputeVisualLayout_DoorEnd_ReversesOrder()
    {
        var map = new SeatMap { DoorSide = SeatSideOrientation.End };
        SeatMapService.AddColumn(map, 1);
        SeatMapService.AddColumn(map, 1);
        SeatMapService.AddColumn(map, 1);

        var layout = SeatMapService.ComputeVisualLayout(map);

        Assert.Equal(3, layout.Count);
        Assert.Equal(2, layout[0].LogicalIndex);
        Assert.Equal(1, layout[1].LogicalIndex);
        Assert.Equal(0, layout[2].LogicalIndex);
    }

    [Fact]
    public void ComputeVisualLayout_DoorEnd_MixedAislesAssignedToPreviousLogicalColumn()
    {
        // 5 列 c0..c4，c1、c3 有过道；镜像后视觉布局应为 [c4][aisle][c3][c2][aisle][c1][c0]
        var map = new SeatMap { DoorSide = SeatSideOrientation.End };
        for (var i = 0; i < 5; i++)
        {
            SeatMapService.AddColumn(map, 1);
        }
        map.Columns[1].HasAisleAfter = true;
        map.Columns[3].HasAisleAfter = true;

        var layout = SeatMapService.ComputeVisualLayout(map);

        Assert.Equal(5, layout.Count);
        Assert.Equal((LogicalIndex: 4, HasAisleAfterVisual: true), layout[0]);  // [c4][aisle]
        Assert.Equal((LogicalIndex: 3, HasAisleAfterVisual: false), layout[1]); // [c3]
        Assert.Equal((LogicalIndex: 2, HasAisleAfterVisual: true), layout[2]);  // [c2][aisle]
        Assert.Equal((LogicalIndex: 1, HasAisleAfterVisual: false), layout[3]); // [c1]
        Assert.Equal((LogicalIndex: 0, HasAisleAfterVisual: false), layout[4]); // [c0]
    }

    [Fact]
    public void ComputeVisualLayout_DoorEnd_FirstLogicalColumnNeverHasAisle()
    {
        var map = new SeatMap { DoorSide = SeatSideOrientation.End };
        SeatMapService.AddColumn(map, 1);
        SeatMapService.AddColumn(map, 1);
        map.Columns[0].HasAisleAfter = true;
        map.Columns[1].HasAisleAfter = true;

        var layout = SeatMapService.ComputeVisualLayout(map);

        Assert.Equal(2, layout.Count);
        Assert.Equal((1, true), layout[0]);  // 渲染逻辑列 1 时上一逻辑列 c0 有过道 → [c1][aisle]
        Assert.Equal((0, false), layout[1]); // 逻辑列 0 自身永不过道
    }

    [Fact]
    public void ComputeVisualLayout_EmptyAndSingleColumn_DoesNotThrow()
    {
        var empty = new SeatMap { DoorSide = SeatSideOrientation.End };
        Assert.Empty(SeatMapService.ComputeVisualLayout(empty));

        var single = new SeatMap { DoorSide = SeatSideOrientation.End };
        SeatMapService.AddColumn(single, 1);
        var layout = SeatMapService.ComputeVisualLayout(single);
        Assert.Single(layout);
        Assert.Equal((0, false), layout[0]);
    }
}

public sealed class SeatFilterDrawFilterTests
{
    private static List<Member> CreateMembers(params string[] names) =>
        names.Select(name => new Member { Name = name }).ToList();

    [Fact]
    public void Apply_WithSeatFilter_LimitsToRegionMembers()
    {
        var members = CreateMembers("张三", "李四", "王五", "赵六");
        var map = new SeatMap();
        SeatMapService.AddColumn(map, 2);
        SeatMapService.AddColumn(map, 2);
        SeatMapService.Assign(map, 0, 0, "张三");
        SeatMapService.Assign(map, 0, 1, "李四");
        SeatMapService.Assign(map, 1, 0, "王五");
        SeatMapService.Assign(map, 1, 1, "赵六");

        // 靠门半区 = 第 1 列
        var result = DrawFilterService.Apply(
            members,
            [],
            [],
            new DrawFilterOptions(SeatFilter: new SeatFilterOptions(SeatRegion.DoorHalf)),
            map);

        Assert.Equal(["张三", "李四"], result.Select(m => m.Name).ToList());
    }

    [Fact]
    public void Apply_WithSpecificColumn_FiltersByColumn()
    {
        var members = CreateMembers("张三", "李四", "王五");
        var map = new SeatMap();
        SeatMapService.AddColumn(map, 1);
        SeatMapService.AddColumn(map, 1);
        SeatMapService.Assign(map, 0, 0, "张三");
        SeatMapService.Assign(map, 1, 0, "李四");
        SeatMapService.Assign(map, 0, 0, "王五"); // 换掉张三

        var result = DrawFilterService.Apply(
            members,
            [],
            [],
            new DrawFilterOptions(SeatFilter: new SeatFilterOptions(SeatRegion.SpecificColumn, ColumnIndex: 1)),
            map);

        Assert.Equal(["李四"], result.Select(m => m.Name).ToList());
    }

    [Fact]
    public void Apply_WithSeatFilter_UnseatedMembersExcluded()
    {
        var members = CreateMembers("张三", "未排座");
        var map = new SeatMap();
        SeatMapService.AddColumn(map, 1);
        SeatMapService.Assign(map, 0, 0, "张三");

        var result = DrawFilterService.Apply(
            members,
            [],
            [],
            new DrawFilterOptions(SeatFilter: new SeatFilterOptions(SeatRegion.DoorHalf)),
            map);

        Assert.Single(result);
        Assert.Equal("张三", result[0].Name);
    }
}

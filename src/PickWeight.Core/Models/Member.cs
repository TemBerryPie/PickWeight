using System.Text.Json.Serialization;

namespace PickWeight.Core.Models;

public sealed class Member
{
 public string Name { get; set; } = string.Empty;
 public MemberGender? Gender { get; set; }
 public double ExplicitWeight { get; set; } = 100d;
 [JsonIgnore]
 public double ImplicitWeight { get; set; } = 1d;
 public int CalledCount { get; set; }
 public bool IsWeightLinked { get; set; } = true;
 public bool IsActive { get; set; } = true;

 public Member Clone()
 {
 return new Member
 {
 Name = Name,
 Gender = Gender,
 ExplicitWeight = ExplicitWeight,
 ImplicitWeight = ImplicitWeight,
 CalledCount = CalledCount,
 IsWeightLinked = IsWeightLinked,
 IsActive = IsActive
 };
 }
}

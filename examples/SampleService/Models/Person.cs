using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SampleService.Models;

public class Person
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; init; }

    [MaxLength(100)]
    [Required]
    public string Name { get; init; } = string.Empty;
}

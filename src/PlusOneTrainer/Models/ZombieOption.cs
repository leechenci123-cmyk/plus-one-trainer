using PlusOneTrainer.Services;

namespace PlusOneTrainer.Models;

public sealed record ZombieOption(int Id, string Chinese, string English, bool Experimental = false)
{
    public string DisplayName => LocalizationService.CurrentLanguage == "en-US"
        ? $"{Id:00} · {English}"
        : $"{Id:00} · {Chinese}";

    public static IReadOnlyList<ZombieOption> All { get; } =
    [
        new(0, "普通僵尸", "Zombie"),
        new(1, "旗帜僵尸", "Flag Zombie"),
        new(2, "路障僵尸", "Conehead Zombie"),
        new(3, "撑杆僵尸", "Pole Vaulting Zombie"),
        new(4, "铁桶僵尸", "Buckethead Zombie"),
        new(5, "读报僵尸", "Newspaper Zombie"),
        new(6, "铁栅门僵尸", "Screen Door Zombie"),
        new(7, "橄榄球僵尸", "Football Zombie"),
        new(8, "舞王僵尸", "Dancing Zombie"),
        new(9, "伴舞僵尸", "Backup Dancer", true),
        new(10, "鸭子救生圈僵尸", "Ducky Tube Zombie"),
        new(11, "潜水僵尸", "Snorkel Zombie"),
        new(12, "冰车僵尸", "Zomboni"),
        new(13, "雪橇车僵尸小队", "Zombie Bobsled Team", true),
        new(14, "海豚骑士僵尸", "Dolphin Rider Zombie"),
        new(15, "玩偶匣僵尸", "Jack-in-the-Box Zombie"),
        new(16, "气球僵尸", "Balloon Zombie"),
        new(17, "矿工僵尸", "Digger Zombie"),
        new(18, "跳跳僵尸", "Pogo Zombie"),
        new(19, "雪人僵尸", "Zombie Yeti"),
        new(20, "蹦极僵尸", "Bungee Zombie", true),
        new(21, "扶梯僵尸", "Ladder Zombie"),
        new(22, "投石车僵尸", "Catapult Zombie"),
        new(23, "巨人僵尸", "Gargantuar"),
        new(24, "小鬼僵尸", "Imp"),
        new(25, "僵王博士", "Dr. Zomboss", true),
        new(26, "豌豆射手僵尸", "Peashooter Zombie"),
        new(27, "坚果僵尸", "Wall-nut Zombie"),
        new(28, "火爆辣椒僵尸", "Jalapeno Zombie"),
        new(29, "机枪豌豆僵尸", "Gatling Pea Zombie"),
        new(30, "窝瓜僵尸", "Squash Zombie"),
        new(31, "高坚果僵尸", "Tall-nut Zombie"),
        new(32, "红眼巨人僵尸", "Giga-Gargantuar")
    ];
}

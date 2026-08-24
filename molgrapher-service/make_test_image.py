"""用 RDKit 生成测试分子图（苯 + 咖啡因），用于验证 /recognize。"""

from rdkit import Chem
from rdkit.Chem import Draw

mols = [
    Chem.MolFromSmiles("c1ccccc1"),        # 苯
    Chem.MolFromSmiles("Cn1cnc2c5c1c(=O)n(c(=O)n2C)C5"),  # 咖啡因
]

for i, m in enumerate(mols):
    img = Draw.MolToImage(m, size=(512, 512))
    img.save(f"test_mol_{i}.png")
    print(f"test_mol_{i}.png saved")

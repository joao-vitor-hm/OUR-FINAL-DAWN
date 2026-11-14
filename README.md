# OUR FINAL DAWN

### Um mundo. Três épocas. Diferentes histórias de sobrevivência.

**Jogo independente de sobrevivência em desenvolvimento na Unity 6.**

Exploração, narrativa interligada e sistemas de movimento físico em terceira pessoa.

**Status:** Protótipo técnico em desenvolvimento  
**Linguagem:** C#  
**Engine:** Unity 6

---

## Sobre o projeto

**OUR FINAL DAWN** é um projeto independente de jogo de sobrevivência em mundo aberto, criado por **João Vitor**, sob o nome **Hyperion Studios**.

A proposta é explorar uma mesma cidade em **três períodos de sua história**, com perspectivas, acontecimentos e possibilidades de exploração diferentes.

Cada período terá sua própria narrativa, que poderá ser acompanhada de forma independente, enquanto conexões entre as histórias ampliarão a compreensão do universo.

O projeto busca combinar ambientação, liberdade de exploração e personagens com respostas físicas às condições do ambiente.

## Três períodos, um mesmo mundo

| Período | Proposta narrativa |
| --- | --- |
| **Antes do colapso** | A cidade em funcionamento, com conflitos e acontecimentos que antecedem a crise. |
| **Início do surto** | Os primeiros efeitos da epidemia e a luta para sobreviver à transformação da cidade. |
| **Anos depois** | Um mundo marcado pelo abandono, pela natureza e pelas consequências do passado. |

As histórias foram concebidas para funcionar de maneira independente, com referências e descobertas que conectam os três períodos **sem exigir que o jogador conclua os demais**.

## Estado atual do desenvolvimento

**Este repositório contém um protótipo em desenvolvimento, não uma versão jogável final.** Mecânicas, código, modelos e objetivos podem mudar durante a produção.

### Sistemas presentes no protótipo

- **Controle em terceira pessoa:** movimentação relativa à câmera e integração com o Unity Input System.
- **Active Ragdoll experimental:** corpo físico utilizando `Rigidbody`, `Collider` e `ConfigurableJoint`.
- **Postura e equilíbrio:** controladores que tentam manter o personagem estável durante a simulação física.
- **Passos procedurais e IK:** testes de posicionamento dos pés e orientação dos membros por alvos.
- **Ferramentas de depuração:** visualizações e indicadores para analisar o comportamento do personagem.

Esses sistemas ainda estão sendo integrados e ajustados. **Estabilidade, caminhada natural, colisões e recuperação de quedas** são prioridades de desenvolvimento.

### Objetivos planejados

- Exploração de uma cidade em mundo aberto e suas diferentes épocas.
- Mecânicas de sobrevivência, coleta e interação com o ambiente.
- Personagens com reações físicas a impactos, terreno e condições corporais.
- Comportamento de NPCs e infectados integrado à proposta de sobrevivência.
- Experiências online e sistemas multiplayer.
- Expansão progressiva das histórias, áreas e atividades disponíveis.

*Esses itens representam a direção do projeto e não funcionalidades disponíveis atualmente.*

## Tecnologias

| Tecnologia | Aplicação |
| --- | --- |
| **Unity 6** | Desenvolvimento e simulação do jogo |
| **C#** | Implementação dos sistemas |
| **Unity Input System** | Entrada e controle do jogador |
| **Cinemachine** | Câmera em terceira pessoa |
| **Rigidbody / ConfigurableJoint** | Física corporal e controle das articulações |
| **Blender** | Criação e ajuste de modelos e equipamentos 3D |

### Organização técnica

Alguns dos componentes em desenvolvimento incluem:

- `Player` — integração do personagem e de seu corpo físico.
- `ActiveRagdollMuscle` — músculos virtuais e alvos articulares.
- `ProceduralPosture` — orientação e correção de postura.
- `ProceduralBalanceMotor` — equilíbrio e deslocamento físico.
- `ProceduralBalance` / `GroundContact` — massa, apoio e contato com o solo.
- `GameControlls` — entradas de movimento.

A arquitetura pode mudar à medida que os sistemas forem consolidados.

## Como abrir o projeto

Esta seção é destinada a pessoas autorizadas a acessar o código e a estudar sua estrutura.

1. Instale o **Unity Hub** e a versão indicada em `ProjectSettings/ProjectVersion.txt`.
2. Clone o repositório com o Git:

   `git clone https://github.com/joao-vitor-hm/OUR-FINAL-DAWN.git`

3. No Unity Hub, selecione **Add project from disk** e escolha a pasta clonada.
4. Aguarde a importação dos assets e das dependências.
5. Abra a cena de desenvolvimento disponível e confira o Console.

Ainda não há garantia de uma experiência jogável.

## Próximas prioridades

1. Estabilizar o Active Ragdoll em repouso e durante a movimentação.
2. Melhorar a transferência de peso, o contato dos pés e a recuperação de equilíbrio.
3. Consolidar interação, colisões e controles básicos.
4. Construir uma pequena área explorável para testes.
5. Evoluir os sistemas de sobrevivência e a estrutura do mundo.

## Comunidade

O projeto está em desenvolvimento e ideias construtivas são bem-vindas.

- [Discussões do projeto](https://github.com/joao-vitor-hm/OUR-FINAL-DAWN/discussions)
- [Relatar problemas técnicos](https://github.com/joao-vitor-hm/OUR-FINAL-DAWN/issues)

A disponibilidade desses recursos depende das configurações do repositório.

## Autoria e direitos

**Desenvolvimento:** João Vitor  
**Projeto:** Hyperion Studios

**© 2026 João Vitor. Todos os direitos reservados.**

Este repositório é disponibilizado para **consulta e estudo**.

Na ausência de uma licença expressa, não são concedidas permissões gerais para copiar, modificar, redistribuir ou explorar comercialmente o código e os materiais originais.

Recursos de terceiros permanecem sujeitos às respectivas licenças.

---

**OUR FINAL DAWN** — *Um projeto independente, construído passo a passo.*
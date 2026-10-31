# LastChaos ToolBox <img align="left" src="https://user-images.githubusercontent.com/5092697/138568453-9cbbedb8-7889-4a9d-ac72-5d2dae9bae9f.png" width="100px">
It provides the basics for creating tools to manage Databases and perhaps files related to LastChaos.

For this game's schema requirements, transactional saves, and the workflow to
capture editor changes in Git, see [database compatibility and version control](database/README.md).
The unfinished Quest Editor is unavailable; its copied write handlers targeted rare options.

# Concept of Global Tables
* The idea behind the project is to have a fast and efficient tool in terms of requests to the DataBase Server. With that in mind I designed a scheme in which there are __Global Tables__, these are populated for the first time by the Tool that requires the information, and later said information can be used by another tool, Avoiding constants requests each time some Tool open.

* When a Tool populates a __Global Table__, it is not necessarily populated with all the information available in the Database, but rather the system is designed so that different tools request only the columns necessary for the operation of said Tool. Being able to load different columns by different Tools without the information overlapping or replacing.

* Finally, when in a Tool the operator decides to apply changes made, an attempt is made to execute a Query either type UPDATE or INSERT, in case of success, changes are updated in the Global Tables as well.

# Hardcoded Definitions and Information related to Server/Client is stored in
* [Definitions.cs](Definitions.cs)

# Definitions to fast adaptation to each Source code
* Many Forms have definitions in their code. Many tools are adapted to my source code, but you can also disable these adaptations (or create the ones you need).

# Help Dialogs
* Pickers
1) [Flag Picker Implementation Example](Pickers/FlagPicker.cs)
2) [Icon Picker Implementation Example](Pickers/IconPicker.cs)
3) [Skill Picker Implementation Example](Pickers/SkillPicker.cs)
4) [Item Picker Implementation Example](Pickers/ItemPicker.cs)
5) [Title Picker Implementation Example](Pickers/RareOptionPicker.cs)
6) [String Picker Implementation Example](Pickers/StringPicker.cs)
7) [Option Picker Implementation Example](Pickers/OptionPicker.cs)
8) [Magic Picker Implementation Example](Pickers/MagicPicker.cs)
9) [Generic Type Picker Implementation Example](Pickers/GenericTypePicker.cs) (Some Items require a Type index (For example IETC_UPGRADE_GENERAL) in a_num_0, so I made this generic picker to select index from some list.)
10) [Special Skill Picker Implementation Example](Pickers/SpecialSkillPicker.cs)
11) [NPC Picker Implementation Example](Pickers/NPCPicker.cs)
12) [Quest Picker Implementation Example](Pickers/QuestPicker.cs)
13) [Zone Picker Implementation Example](Pickers/ZonePicker.cs)
14) [Title Picker Implementation Example](Pickers/TitlePicker.cs)

- NOTE: Picker Dialogs are intended to complement larger Tools, however, they have the ability to request information autonomously. This means that you can make the decision to load, for example, t_skill names to be able to display them in your Tool, which will increase loading times. On the other hand, you could not request this information and work only with IDS, but when invoking the Skill Picker Dialog, it will request data such as the name and description of the skills automatically if necessary.

* Others
1) [MessageBox With Captions & ProgressBar Implementation Example](MessageBoxes/MessageBox_Progress.cs)
2) [MessageBox with Text Input Implementation Example](MessageBoxes/MessageBox_Input.cs)
3) [MessageBox with ComboBox Implementation Example](MessageBoxes/MessageBox_ComboBox.cs)

# Helper Functions
1) [AskForIndex](Main.cs)
2) [EscapeChars](Main.cs)
3) [GetIcon](Main.cs)
4) [GetWorldMap](Main.cs)
5) [GetGoldColor](Main.cs)
6) [AT_MIX](Main.cs)
7) [GET_AT_VAR](Main.cs)
8) [GET_AT_LV](Main.cs)
9) [AT_ADMIX](Main.cs)
10) [GET_AT_DEF](Main.cs)
11) [GET_AT_ATT](Main.cs)

# Generic Data Loaders
1) [GenericLoadZoneDataAsync](Main.cs)
2) [GenericLoadStringDataAsync](Main.cs)
3) [GenericLoadSkillDataAsync](Main.cs)
4) [GenericLoadSkillLevelDataAsync](Main.cs)
5) [GenericLoadQuestDataAsync](Main.cs)
6) [GenericLoadItemDataAsync](Main.cs)
7) [GenericLoadNPCDataAsync](Main.cs)
8) [GenericLoadOptionDataAsync](Main.cs)

# Showcase
## Item Editor
![1](https://github.com/user-attachments/assets/fc7f7ec9-bc47-421d-ac2a-3ce9972322cb)

## Control Panel
![2](https://github.com/user-attachments/assets/861eb420-034f-4aa9-917e-f5bd76a6b6e7)

## Strings & Lods Exporter
![3](https://github.com/user-attachments/assets/cdcc24ea-4b86-4bc7-8a36-ca8cd1a77a2e)


## Transmutation Crucible

Open **Professions Editor → Transmutation** after applying game migration **0045**.
The editor never creates database schema or changes character balances or queues.

- **Upgrading items:** choose an ordinary upgrading material, output quantity,
  point price and time per operation. The initial Heaven Stone offer costs 1,000
  points and takes two minutes. The preview explains bulk cost and sequential time.
  Paid batches keep their accepted item, price and duration after edits or removal.
- **Reservoir and equipment value:** set the character point capacity and the
  numerator/denominator of `floor(base equipment level × numerator / denominator)`.
  The minimum is one point. An overflowing conversion is refused in full; lowering
  capacity does not remove points already stored. The cap applies to unspent points.
- **Equipment exceptions:** exclude an existing item, or override its point value.
  A fixed value of zero uses the formula. Exceptions cannot enable unsafe equipment,
  gathering tools, costumes, rented items or linked equipment. Enhancement gives no
  extra points and is destroyed with the selected equipment.
- **Crucible:** configure separate construction/activity switches, the named Artisan
  Rank prerequisite, built/plot model paths, names, position, heading and use distance.
  The initial construction requirement is permanent Rank ID 2, not display order 2.
- **Construction costs:** edit up to eight different one-time materials. An empty
  list permits free construction. Already-built workshops remain owned.

The **Artisan Ranks** page shows facility construction rewards and links directly
back to their settings. Reopen the Crucible/plot after Save; placement/model changes
may require re-entering the private hall. New artwork needs normal client deployment.

**Record Content Changes** includes all five authored `t_transmutation_*` tables:
settings, facility, facility ingredients, offers and item exceptions. Character
wallets, buildings, paid work orders, delivery records and receipts are explicitly
excluded, including future unreviewed Transmutation tables. Exporting records content
in the selected local Git checkout; it does not publish or push it.

export const itemNatureOptions = ["Consumo", "Permanente"];

export const itemConditionOptions = ["Ótimo", "Regular", "Inservível"];

export const locationOptions = [
  "1ª CIA - Administrativo",
  "1ª CIA - Alojamento Feminino",
  "1ª CIA - Alojamento Masculino 1",
  "1ª CIA - CENOP",
  "1ª CIA - Operacional",
  "1ª CIA - Rancho",
  "1ª CIA - Sala de Armários",
  "1ª CIA - Sala de Instruções",
  "1ª CIA - Sala de Meios",
  "2ª CIA - Administrativo",
  "2ª CIA - Alojamento Feminino",
  "2ª CIA - Alojamento Masculino",
  "2ª CIA - CENOP",
  "2ª CIA - Comando",
  "2ª CIA - Operacional",
  "2ª CIA - Rancho",
  "2ª CIA - Sala de Meios",
  "Almoxarifado",
  "CENOP",
  "Comando",
  "Corregedoria Setorial",
  "Rancho",
  "Rancho Administração",
  "Sala Auxiliar",
  "Sala de Convivência",
  "Sala de Reuniões",
  "Seção de Logística",
  "Serviços Gerais",
  "SPO",
  "SRHVP",
  "Sub-Comando",
  "Sub-seção de Transporte",
];

export const emptyItemForm = {
  name: "",
  quantity: 0,
  assetTag: "",
  nature: "",
  location: "",
  condition: "",
  notes: "",
  photoDataUrl: "",
  photoPreviewUrl: "",
  hasPhoto: false,
  removePhoto: false,
  isDischarged: false,
};

export const emptyTransferForm = {
  itemId: "",
  quantity: 1,
  fromLocation: "",
  destinationType: "Local",
  toLocation: "",
  destinationPerson: "",
  notes: "",
};

export const emptyNoteForm = {
  title: "",
  content: "",
  tags: "",
};

export const emptyInventoryFilters = {
  name: "",
  assetTag: "",
  condition: "",
  nature: "",
  location: "",
};

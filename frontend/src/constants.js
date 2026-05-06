import catalog from "../../shared/catalog.json";

export const itemNatureOptions = catalog.itemNatureOptions;

export const itemConditionOptions = catalog.itemConditionOptions;

export const locationOptions = catalog.locationOptions;

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
  responsible: "",
  discharged: "",
};

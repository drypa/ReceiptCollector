import { Component, Inject, OnInit } from '@angular/core';
import { FormControl, FormGroup } from '@angular/forms';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { WasteService } from '../waste.service';
import { Waste } from '../waste';
import { EditWasteRequest } from '../contracts/waste/edit-waste-request';

@Component({
  selector: 'app-waste-details',
  templateUrl: './waste-details.component.html',
  styleUrls: ['./waste-details.component.scss']
})
export class WasteDetailsComponent implements OnInit {
  wasteEdit = new FormGroup({
    description: new FormControl(''),
  });

  constructor(@Inject(MAT_DIALOG_DATA) public waste: Waste,
              private wasteService: WasteService) {
  }

  ngOnInit(): void {
  }


  onSubmit() {
    const description = this.wasteEdit.value.description;
    this.wasteService.update(this.waste.id, <EditWasteRequest>{ description: description })
      .subscribe();
  }
}

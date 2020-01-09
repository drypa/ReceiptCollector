import {Component, OnInit} from '@angular/core';
import {NewReceiptComponent} from "../new-receipt/new-receipt.component";
import {MatDialog} from "@angular/material/dialog";

@Component({
  selector: 'app-authorized',
  templateUrl: './authorized.component.html',
  styleUrls: ['./authorized.component.scss']
})
export class AuthorizedComponent implements OnInit {

  constructor(private dialog: MatDialog) {
  }

  ngOnInit() {
  }

  openNewReceiptDialog(): void {
    this.dialog.open(NewReceiptComponent, {
      width: '700px'
    })
  }
}

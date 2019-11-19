import {Component, OnInit} from '@angular/core';
import {FormControl, FormGroup} from '@angular/forms';
import {ReceiptService} from '../receipt.service';
import {MatSnackBar} from '@angular/material/snack-bar';


@Component({
  selector: 'app-new-receipt',
  templateUrl: './new-receipt.component.html',
  styleUrls: ['./new-receipt.component.scss']
})
export class NewReceiptComponent implements OnInit {
  control = new FormControl('');
  newReceiptForm = new FormGroup({
    barCodeText: this.control
  });

  constructor(private receiptService: ReceiptService,
              private snackBar: MatSnackBar) {
  }

  ngOnInit() {
  }

  onSubmit() {
    console.log(this.control.value);
    this.receiptService.addReceiptByBarCode(this.control.value)
      .subscribe(() => {
        this.control.setValue('');
        this.showSnack("Added")
      }, err => this.showSnack("Error"));
  }

  private showSnack(message: string) {
    this.snackBar.open(message, "OK", {})
  }
}
